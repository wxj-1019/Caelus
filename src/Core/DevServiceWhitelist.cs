// @author zenjiro 18967498922@163.com
// 文件用途 编译期间不压的开发常驻服务进程名

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class DevServiceWhitelist
    {
        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Docker
            "docker", "dockerd", "com.docker.backend", "com.docker.service", "vpnkit", "docker-proxy", "containerd",
            // 数据库
            "postgres", "mysqld", "mariadbd", "redis-server", "mongod", "sqlservr", "oracle", "sqlite3",
            // ES/日志栈
            "elasticsearch", "kibana", "logstash", "filebeat", "metricbeat",
            // 消息队列
            "rabbitmq-server", "kafka", "nats-server"
        };

        public static bool Contains(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            string bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4) : processName;
            return Names.Contains(bare);
        }
    }
}
