﻿using System.Collections.Generic;
using ReportService.Interfaces;

namespace ReportService.Core
{
    public class RTaskRunContext : IRTaskRunContext
    {
        public Dictionary<string, string> DataSets { get; set; } = new Dictionary<string, string>();
        public int TaskId { get; set; }
        public string TaskName { get; set; }
        public IDefaultTaskExporter exporter { get; set; }
    }
}