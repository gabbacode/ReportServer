﻿using System.IO;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Newtonsoft.Json;
using Renci.SshNet;
using ReportService.Interfaces.Core;
using ReportService.Interfaces.ReportTask;

namespace ReportService.Operations.DataExporters
{
    public class SshExporter : IOperation
    {
        public CommonOperationProperties Properties { get; set; } = new CommonOperationProperties();
        private readonly IViewExecutor viewExecutor;
        private readonly ILifetimeScope autofac;

        public string FileFolder;
        public string FileName;
        public bool RunIfVoidPackage { get; set; }
        public bool ConvertPackageToXlsx;
        public bool ConvertPackageToJson;
        public bool ConvertPackageToCsv;
        public bool ConvertPackageToXml;
        public string Host;
        public string Login;
        public string Password;
        public string FolderPath;

        public SshExporter(IMapper mapper, ILifetimeScope autofac, SshExporterConfig config)
        {
            mapper.Map(config, this);
            mapper.Map(config, Properties);
            this.autofac = autofac;
            viewExecutor = autofac.ResolveNamed<IViewExecutor>("commonviewex");
        }

        public void Execute(IRTaskRunContext taskContext)
        {
            var package = taskContext.Packages[Properties.PackageName];

            if (!RunIfVoidPackage && package.DataSets.Count == 0)
                return;

            using (var client = new SftpClient(Host, Login, Password))
            {
                client.Connect();

                if (!string.IsNullOrEmpty(FileName))
                    SaveFileToServer(taskContext, package, client);

                if (ConvertPackageToXlsx)
                    SaveXlsxPackageToServer(package, client);

                if (ConvertPackageToJson)
                    SaveJsonPackageToServer(package, client);

                if (ConvertPackageToCsv)
                    SaveCsvPackageToServer(package, client);
            }
        }

        private void SaveXlsxPackageToServer(OperationPackage package, SftpClient client)
        {
            var filenameXlsx = $@"{Properties.PackageName}.xlsx";

            using (var excel = viewExecutor.ExecuteXlsx(package, Properties.PackageName))
            {
                using (var streamXlsx =
                    new MemoryStream())
                {
                    excel.SaveAs(streamXlsx);
                    streamXlsx.Position = 0;
                    client.UploadFile(streamXlsx, Path.Combine(FolderPath, filenameXlsx));
                }
            }
        }

        private void SaveCsvPackageToServer(OperationPackage package, SftpClient client)
        {
            var filenameCsv = $@"{Properties.PackageName}.csv";
            var csvBytes = viewExecutor.ExecuteCsv(package);
            using (var csvStream = new MemoryStream(csvBytes))
                client.UploadFile(csvStream, Path.Combine(FolderPath, filenameCsv));
        }

        private void SaveJsonPackageToServer(OperationPackage package, SftpClient client)
        {
            var filenameJson = $@"{Properties.PackageName}.json";

            using (var streamJson = new MemoryStream(System.Text.Encoding.UTF8
                .GetBytes(JsonConvert.SerializeObject(package))))
            {
                client.UploadFile(streamJson, Path.Combine(FolderPath, filenameJson));
            }
        }

        private void SaveFileToServer(IRTaskRunContext taskContext, OperationPackage package, SftpClient client)
        {
            var fullPath = Path.Combine(FileFolder == "Default folder" ? taskContext.DataFolderPath : FileFolder,
                FileName);

            using (FileStream fstr =
                File.OpenRead(fullPath))
            {
                client.UploadFile(fstr, Path.Combine(FolderPath, FileName));
            }
        }

        public Task ExecuteAsync(IRTaskRunContext taskContext)
        {
            Execute(taskContext);
            return Task.CompletedTask;
        }
    }
}