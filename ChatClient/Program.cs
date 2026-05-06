using System.Net.Sockets;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

string ipAddress = "192.168.31.183";

using var client = new TcpClient();
await client.ConnectAsync(ipAddress, 27001);
Console.WriteLine("Connected");

using var stream = client.GetStream();
var reader = new StreamReader(stream);
var writer = new StreamWriter(stream) { AutoFlush = true };

Console.Write("UserName: ");
string username = Console.ReadLine();
await writer.WriteLineAsync($"Username:{username}");

var response = await reader.ReadLineAsync();

if (response == "NEED_EMAIL")
{
    Console.Write($"Enter Email: ");
    string email = Console.ReadLine();
    await writer.WriteLineAsync($"Email:{email}");
}
else
{
    Console.WriteLine($"Welcome back {username}!");
}

var userMap = new Dictionary<int, int>();

while (true)
{
    Console.WriteLine("1. Show Users");
    Console.WriteLine("2. Go Chat");
    Console.WriteLine("3. Exit from program");
    Console.Write("Choose: ");
    string choice = Console.ReadLine();

    switch (choice)
    {
        case "1":
            await writer.WriteLineAsync("SHOW_USERS");
            string resp = await reader.ReadLineAsync();
            var subject = resp.Split(":")[0];
            var user = resp.Substring(resp.IndexOf(":") + 1);
            var userList = user.Split(',');

            Console.WriteLine($"\n{subject}");
            foreach (var u in userList)
            {
                var line = u.Trim();
                var lineNum = int.Parse(line.Split(".")[0]);
                int id = int.Parse(line.Substring(line.LastIndexOf("[") + 1).Replace("]", ""));
                userMap[lineNum] = id;
                Console.WriteLine($"\t{line.Split("[")[0]}");
            }
            Console.WriteLine();
            break;

        case "2":

            if (userMap.Count == 0)
            {
                Console.WriteLine("No users available to chat.");
                break;
            }

            Console.Write("Line NO: ");
            int lineNo = int.Parse(Console.ReadLine());
            int receiverId = userMap[lineNo];
            await writer.WriteLineAsync($"GO_CHAT:{receiverId}");

            var chatResp = await reader.ReadLineAsync();

            while (chatResp != "CHAT_READY")
            {
                Console.WriteLine(chatResp);
                chatResp = await reader.ReadLineAsync();
            }

            if (chatResp == "CHAT_READY")
            {
                Console.WriteLine("Çat Başladı (Çıxmaq üçün 'exit', fayl üçün 'file' yazın)");

                var cts = new CancellationTokenSource();
                var fileReadySignal = new SemaphoreSlim(0, 1);
                string? fileRespMsg = null;

                var receiveTask = Task.Run(async () =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            var incomingMsg = await reader.ReadLineAsync();

                            if(incomingMsg == "FILE_READY")
                            {
                                fileRespMsg = incomingMsg;
                                fileReadySignal.Release();
                                continue;
                            }

                            if (incomingMsg == null || incomingMsg == "CHAT_CLOSED") break;

                            if (incomingMsg.StartsWith("INCOMING_FILE"))
                            {
                                var parts = incomingMsg.Split(":");
                                string fileName = parts[1];
                                long fileSize = long.Parse(parts[2]);

                                byte[] fileBytes = new byte[fileSize];
                                int totalRead = 0;
                                while (totalRead < fileSize)
                                {
                                    int read = await stream.ReadAsync(fileBytes.AsMemory(totalRead, (int)(fileSize - totalRead)));
                                    if (read == 0) break;
                                    totalRead += read;
                                }

                                var saveDir = Path.Combine("C:\\Users\\ProUser\\Desktop\\", "ClientFile");

                                Directory.CreateDirectory(saveDir);
                                var savePath = Path.Combine(saveDir, fileName);

                                await using var fileStream =
                                        new FileStream
                                        (
                                            savePath,
                                            FileMode.Create,
                                            FileAccess.Write,
                                            FileShare.None,
                                            bufferSize: 8192,
                                            useAsync: true
                                        );

                                await fileStream.WriteAsync(fileBytes, 0, totalRead);

                                Console.WriteLine($"\nFile received: {fileName}");
                            }
                            else
                            {
                                Console.WriteLine($"\n{incomingMsg}");
                            }
                        }
                        catch { }
                    }
                });

                while (true)
                {
                    Console.Write("Message: ");
                    string msg = Console.ReadLine();

                    if (msg == "exit")
                    {
                        await writer.WriteLineAsync("EXIT_CHAT");
                        break;
                    }
                    else if (msg == "file")
                    {
                        Console.Write("Enter File Path: ");
                        string filePath = Console.ReadLine();

                        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                        {
                            Console.WriteLine("File not found");
                            return;
                        }

                        string fileName = Path.GetFileName(filePath);
                        long fileSize = new FileInfo(filePath).Length;

                        await writer.WriteLineAsync($"FILE_META:{receiverId}:{fileName}:{fileSize}");

                        await fileReadySignal.WaitAsync();                    


                        if (fileRespMsg != "FILE_READY")
                        {
                            Console.WriteLine($"Xəta: {fileRespMsg}");
                            break;
                        }

                        byte[] buffer = new byte[8192];
                        long totalSent = 0;

                        await using var fileStream =
                            new FileStream
                            (
                                filePath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                bufferSize: 8192,
                                useAsync: true
                            );

                        int read;

                        while ((read = await fileStream.ReadAsync(buffer)) > 0)
                        {
                            await stream.WriteAsync(buffer.AsMemory(0, read));
                            totalSent += read;

                            Console.WriteLine($"\rProgress {(double)totalSent * 100 / fileSize:F2}%");
                        }
                        await writer.WriteLineAsync($"MESSAGE:{receiverId}:File:{fileName}");
                        Console.WriteLine("\nFile sent successfully");
                    }
                    else if (msg.Any())
                    {
                        await writer.WriteLineAsync($"MESSAGE:{receiverId}:Text:{msg}");
                    }
                }

                await receiveTask;
            }
            else
            {
                Console.WriteLine($"{chatResp}");
            }
            break;

        case "3":
            await writer.WriteLineAsync("EXIT_FROM_PROGRAM");
            var chap = await reader.ReadLineAsync();
            Console.WriteLine(chap);
            return;

        default:
            break;
    }
}




