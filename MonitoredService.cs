using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace port_minitor
{
    public class MonitoredService
    {
        public int Id { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public int Port { get; set; }
        public bool IsOnline { get; set; }
        public int RestartAttempts { get; set; } // 重启尝试次数
    }

}
