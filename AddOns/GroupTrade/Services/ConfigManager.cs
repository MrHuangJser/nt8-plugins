using System;
using System.IO;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.AddOns.GroupTrade.Models;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Services
{
    /// <summary>
    /// 配置管理器：负责配置的持久化
    /// </summary>
    public class ConfigManager
    {
        private readonly string _configPath;
        private readonly XmlSerializer _serializer;

        public ConfigManager()
        {
            _configPath = GetConfigPath();
            _serializer = new XmlSerializer(typeof(CopyConfiguration));
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        public CopyConfiguration Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    return CopyConfiguration.CreateDefault();
                }

                using (var reader = new StreamReader(_configPath))
                {
                    var config = (CopyConfiguration)_serializer.Deserialize(reader);
                    return config ?? CopyConfiguration.CreateDefault();
                }
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"[GroupTrade] 加载配置失败: {ex.Message}", PrintTo.OutputTab1);
                return CopyConfiguration.CreateDefault();
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public bool Save(CopyConfiguration config)
        {
            if (config == null)
                return false;

            try
            {
                EnsureDirectory();
                config.LastModified = DateTime.Now;

                using (var writer = new StreamWriter(_configPath))
                {
                    _serializer.Serialize(writer, config);
                }

                NinjaTrader.Code.Output.Process("[GroupTrade] 配置已保存", PrintTo.OutputTab1);
                return true;
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"[GroupTrade] 保存配置失败: {ex.Message}", PrintTo.OutputTab1);
                return false;
            }
        }

        /// <summary>
        /// 导出配置到指定路径
        /// </summary>
        public bool Export(CopyConfiguration config, string filePath)
        {
            if (config == null || string.IsNullOrEmpty(filePath))
                return false;

            try
            {
                using (var writer = new StreamWriter(filePath))
                {
                    _serializer.Serialize(writer, config);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从指定路径导入配置
        /// </summary>
        public CopyConfiguration Import(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    return (CopyConfiguration)_serializer.Deserialize(reader);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取配置文件路径
        /// </summary>
        private string GetConfigPath()
        {
            string userDataDir = NinjaTrader.Core.Globals.UserDataDir;
            string configDir = Path.Combine(userDataDir, "GroupTrade");
            return Path.Combine(configDir, "config.xml");
        }

        /// <summary>
        /// 确保配置目录存在
        /// </summary>
        private void EnsureDirectory()
        {
            string dir = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
