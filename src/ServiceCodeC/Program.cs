using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace ServiceCodeC
{
    class Program
    {
        static readonly ServiceParser ServiceParser = new ServiceParser();
        static readonly ServiceGenerator ServiceGenerator = new ServiceGenerator();
        static void Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("wrong args");
                Console.WriteLine("[usage:]");
                Console.WriteLine("assemblyFile formaterPath clientFilePath serverFilePath");

                return;
            }

            var assemblyFile = args[0];
            var formaterPath = args[1];
            var clientFilePath = args[2];
            var serverFilePath = args[3];

            try
            {
                var assembly = Assembly.LoadFrom(assemblyFile);

                var ast = ServiceParser.Parse(assembly);

                GenerateFile(clientFilePath, ServiceGenerator.GenClient(ast));
                GenerateFile(serverFilePath, ServiceGenerator.GenServer(ast));

                FormatFile(formaterPath, clientFilePath);
                FormatFile(formaterPath, serverFilePath);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        static void GenerateFile(string filePath, string content)
        {
            var fileStream = new FileStream(filePath, FileMode.Create);
            var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);

            streamWriter.Write(content);
            streamWriter.Flush();

            streamWriter.Close();
            fileStream.Close();
        }

        static void FormatFile(string toolPath, string toFormatFilePath)
        {
            var arg = Path.GetFullPath(toFormatFilePath);
            var startInfo = new ProcessStartInfo(toolPath, string.Format("-f {0}", arg));

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;

            Process.Start(startInfo);
        }
    }
}
