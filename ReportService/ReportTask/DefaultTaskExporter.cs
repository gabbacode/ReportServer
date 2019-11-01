﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using Autofac;
using ReportService.Entities.ServiceSettings;
using ReportService.Interfaces.Core;
using ReportService.Interfaces.ReportTask;

namespace ReportService.ReportTask
{
    public class DefaultTaskExporter : IDefaultTaskExporter
    {
        private readonly IViewExecutor executor;
        private readonly string smtpServer;
        private readonly string fromAddress;
        private readonly string administrativeAddresses;

        public DefaultTaskExporter(ILifetimeScope autofac, ServiceConfiguration serviceConfig)
        {
            executor = autofac.ResolveNamed<IViewExecutor>("CommonTableViewEx");
            smtpServer = serviceConfig.EmailSenderSettings.SMTPServer;
            fromAddress = serviceConfig.EmailSenderSettings.From;
            administrativeAddresses = serviceConfig.AdministrativeAddresses;
        }

        public string GetDefaultPackageView(string taskName, OperationPackage package)
        {
            return executor.ExecuteHtml(taskName, package);
        }

        public void SendError(List<Tuple<Exception, string>> exceptions, string taskName)
        {
            using (var client = new SmtpClient(smtpServer, 25))
            using (var msg = new MailMessage())
            {
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                msg.From = new MailAddress(fromAddress);

                foreach (var addr in administrativeAddresses
                    .Split(';'))
                {
                    msg.To.Add(new MailAddress(addr));
                }

                msg.Subject = $"Errors occured in task {taskName} at" +
                              $" {DateTime.Now:dd.MM.yy HH:mm}";

                msg.IsBodyHtml = true;

                List<ColumnInfo> columns = new List<ColumnInfo>
                {
                    new ColumnInfo
                    {
                        Name = "Operation",
                        Type = ScalarType.String
                    },
                    new ColumnInfo
                    {
                        Name = "Message",
                        Type = ScalarType.String
                    },
                    new ColumnInfo
                    {
                        Name = "Trace",
                        Type = ScalarType.String
                    },
                    new ColumnInfo
                    {
                        Name = "Source",
                        Type = ScalarType.String
                    }
                };

                var rows = exceptions.Select(pair => new Row
                {
                    Values =
                    {
                        new VariantValue {StringValue = pair.Item2},
                        new VariantValue {StringValue = pair.Item1.Message},
                        new VariantValue {StringValue = pair.Item1.StackTrace ?? ""},
                        new VariantValue {StringValue = pair.Item1.Source ?? ""}
                    }
                });

                var exceptionsPack = new OperationPackage
                {
                    DataSets =
                    {
                        new DataSet
                        {
                            Columns = {columns},
                            Rows = {rows}
                        }
                    }
                };

                msg.Body =
                    executor.ExecuteHtml("Errors list", exceptionsPack);

                client.Send(msg);
            }
        } //method

        public void ForceSend(string defaultView, string taskName, string mailAddress)
        {
            using (var client = new SmtpClient(smtpServer, 25))
            using (var msg = new MailMessage())
            {
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                msg.From = new MailAddress(fromAddress);

                msg.To.Add(new MailAddress(mailAddress));

                msg.Subject = taskName + $" {DateTime.Now:dd.MM.yy HH:mm}";

                msg.IsBodyHtml = true;

                msg.Body = defaultView;

                client.Send(msg);
            }
        } //method
    }
}