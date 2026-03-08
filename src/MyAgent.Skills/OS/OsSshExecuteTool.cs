using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using Newtonsoft.Json.Linq;
using Renci.SshNet;

namespace MyAgent.Skills.OS
{
    [SkillAction("os.ssh_execute")]
    public class OsSshExecuteTool : IActionTool
    {
        private readonly ILogger<OsSshExecuteTool> _logger;

        public OsSshExecuteTool(ILogger<OsSshExecuteTool> logger)
        {
            _logger = logger;
        }

        public string ActionType => "os.ssh_execute";

        public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
        {
            /*
             * 预期参数:
             * host (string): 目标 IP
             * port (int): 目标端口，默认 22
             * username (string): 登录名
             * password (string): 密码
             * commands (string[]): 依次执行的 Bash / Shell 指令序列
             * file_writes (object[]): 需要通过 SFTP 送入服务器的文件数组 [{ "path": "/root/test.txt", "content": "hello" }]
             */
             
            string host = parameters["host"]?.ToString() ?? "";
            int port = parameters["port"]?.ToObject<int>() ?? 22;
            string username = parameters["username"]?.ToString() ?? "root";
            string password = parameters["password"]?.ToString() ?? "";
            
            var commands = parameters["commands"] as JArray;
            var fileWrites = parameters["file_writes"] as JArray;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogError("[SSH] Missing credentials: Host / Username / Password.");
                return ActionResult.Fail("缺少主机/账号/密码");
            }

            try
            {
                ConnectionInfo connectionInfo = new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password));

                _logger.LogInformation($"[SSH] Connecting to {username}@{host}:{port}...");
                
                // 1. 文件传输 (如果存在) - Sftp
                if (fileWrites != null && fileWrites.Count > 0)
                {
                    using (var sftp = new SftpClient(connectionInfo))
                    {
                        sftp.Connect();
                        _logger.LogInformation($"[SFTP] Connected. Preparing to write {fileWrites.Count} files...");
                        
                        foreach(var file in fileWrites)
                        {
                            string targetPath = file["path"]?.ToString();
                            string content = file["content"]?.ToString();
                            
                            if (string.IsNullOrEmpty(targetPath)) continue;
                            
                            // Ensure directory exists
                            var dirPath = Path.GetDirectoryName(targetPath).Replace("\\", "/");
                            if (!string.IsNullOrEmpty(dirPath) && dirPath != "/")
                            {
                                // We can use SSH command to mkdir -p before sending file
                                using (var sshForMkdir = new SshClient(connectionInfo))
                                {
                                    sshForMkdir.Connect();
                                    sshForMkdir.RunCommand($"mkdir -p \"{dirPath}\"");
                                    sshForMkdir.Disconnect();
                                }
                            }

                            using (var memStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content ?? "")))
                            {
                                sftp.UploadFile(memStream, targetPath, true);
                                _logger.LogInformation($"[SFTP] Wrote file: {targetPath} ({memStream.Length} bytes)");
                            }
                        }
                        sftp.Disconnect();
                    }
                }
                
                // 2. 指令执行 - SSH
                if (commands != null && commands.Count > 0)
                {
                    using (var ssh = new SshClient(connectionInfo))
                    {
                        ssh.Connect();
                        _logger.LogInformation($"[SSH] Executing {commands.Count} commands...");
                        
                        foreach(var cmdToken in commands)
                        {
                            string cmd = cmdToken.ToString();
                            _logger.LogInformation($"[SSH] > {cmd}");
                            context.Logger?.Invoke($"[SSH] > {cmd}");
                            
                            using (var cmdObj = ssh.CreateCommand(cmd))
                            {
                                var result = cmdObj.Execute();
                                if (!string.IsNullOrWhiteSpace(result))
                                {
                                    _logger.LogInformation($"[SSH stdout]\n{result}");
                                    context.Logger?.Invoke($"[SSH stdout]\n{result}");
                                }
                                if (!string.IsNullOrWhiteSpace(cmdObj.Error))
                                {
                                    _logger.LogWarning($"[SSH stderr]\n{cmdObj.Error}");
                                    context.Logger?.Invoke($"[SSH stderr]\n{cmdObj.Error}");
                                }
                                _logger.LogInformation($"[SSH exit code] {cmdObj.ExitStatus}");
                                context.Logger?.Invoke($"[SSH exit code] {cmdObj.ExitStatus}");
                            }
                        }
                        ssh.Disconnect();
                    }
                }

                _logger.LogInformation("[SSH] All execution sessions closed successfully.");
                return ActionResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SSH Error] {ex.Message}\n{ex.StackTrace}");
                return ActionResult.Fail(ex.Message);
            }
        }
    }
}
