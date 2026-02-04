using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.GroupTrade.Core
{
    /// <summary>
    /// 跨合约映射器：支持 Mini ↔ Micro 合约自动转换
    /// </summary>
    public class CrossOrderMapper
    {
        #region Fields

        private readonly Dictionary<string, CrossOrderPair> _pairsByMini;
        private readonly Dictionary<string, CrossOrderPair> _pairsByMicro;
        private readonly List<CrossOrderPair> _allPairs;

        #endregion

        #region Constructor

        public CrossOrderMapper()
        {
            _pairsByMini = new Dictionary<string, CrossOrderPair>(StringComparer.OrdinalIgnoreCase);
            _pairsByMicro = new Dictionary<string, CrossOrderPair>(StringComparer.OrdinalIgnoreCase);
            _allPairs = new List<CrossOrderPair>();

            Initialize();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// 初始化预定义的合约对
        /// </summary>
        private void Initialize()
        {
            // 股指期货
            AddPair("ES", "MES", 10, "E-mini S&P 500");
            AddPair("NQ", "MNQ", 10, "E-mini NASDAQ 100");
            AddPair("YM", "MYM", 10, "E-mini Dow");
            AddPair("RTY", "M2K", 10, "E-mini Russell 2000");

            // 能源期货
            AddPair("CL", "MCL", 10, "Crude Oil");
            AddPair("CL", "QM", 2, "Crude Oil (E-mini)");
            AddPair("NG", "QG", 4, "Natural Gas");
            AddPair("RB", "QU", 2, "RBOB Gasoline");
            AddPair("HO", "QH", 2, "Heating Oil");

            // 贵金属
            AddPair("GC", "MGC", 10, "Gold");
            AddPair("SI", "SIL", 5, "Silver");

            // 外汇期货
            AddPair("6E", "M6E", 8, "Euro FX");
            AddPair("6J", "M6J", 8, "Japanese Yen");
            AddPair("6A", "M6A", 8, "Australian Dollar");
            AddPair("6B", "M6B", 8, "British Pound");
            AddPair("6C", "M6C", 8, "Canadian Dollar");
            AddPair("6S", "M6S", 8, "Swiss Franc");

            // 农产品
            AddPair("ZS", "YK", 10, "Soybeans");
            AddPair("ZC", "XC", 10, "Corn");
            AddPair("ZW", "YW", 10, "Wheat");

            // 欧洲期货 (Eurex)
            AddPair("FDAX", "FDXM", 5, "DAX");
            AddPair("FESX", "FSXE", 10, "Euro Stoxx 50");
        }

        /// <summary>
        /// 添加合约对
        /// </summary>
        private void AddPair(string miniSymbol, string microSymbol, double quantityRatio, string description)
        {
            var pair = new CrossOrderPair
            {
                MiniSymbol = miniSymbol,
                MicroSymbol = microSymbol,
                QuantityRatio = quantityRatio,
                Description = description,
                IsEnabled = true
            };

            _allPairs.Add(pair);
            _pairsByMini[miniSymbol] = pair;
            _pairsByMicro[microSymbol] = pair;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 检查是否可以进行跨合约转换
        /// </summary>
        public bool CanConvert(string sourceSymbol, string targetSymbol)
        {
            if (string.IsNullOrEmpty(sourceSymbol) || string.IsNullOrEmpty(targetSymbol))
                return false;

            // 提取基础 Symbol（去掉月份后缀）
            string sourceBase = ExtractBaseSymbol(sourceSymbol);
            string targetBase = ExtractBaseSymbol(targetSymbol);

            // 检查是否为有效的转换对
            if (_pairsByMini.TryGetValue(sourceBase, out var pair1))
            {
                return pair1.MicroSymbol.Equals(targetBase, StringComparison.OrdinalIgnoreCase);
            }

            if (_pairsByMicro.TryGetValue(sourceBase, out var pair2))
            {
                return pair2.MiniSymbol.Equals(targetBase, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// 获取目标合约的 Instrument
        /// </summary>
        public Instrument GetTargetInstrument(Instrument source, string targetSymbol)
        {
            if (source == null || string.IsNullOrEmpty(targetSymbol))
                return source;

            try
            {
                // 构建目标合约的完整名称
                // 例如：NQ 03-26 → MNQ 03-26
                string sourceFullName = source.FullName;
                string sourceBase = source.MasterInstrument.Name;
                string targetFullName = sourceFullName.Replace(sourceBase, targetSymbol);

                // 获取目标 Instrument
                var targetInstrument = Instrument.GetInstrument(targetFullName);
                return targetInstrument ?? source;
            }
            catch
            {
                return source;
            }
        }

        /// <summary>
        /// 获取手数转换比例
        /// </summary>
        /// <param name="sourceSymbol">源合约 Symbol</param>
        /// <param name="targetSymbol">目标合约 Symbol</param>
        /// <returns>转换比例（源手数 × 比例 = 目标手数）</returns>
        public double GetQuantityRatio(string sourceSymbol, string targetSymbol)
        {
            if (string.IsNullOrEmpty(sourceSymbol) || string.IsNullOrEmpty(targetSymbol))
                return 1.0;

            string sourceBase = ExtractBaseSymbol(sourceSymbol);
            string targetBase = ExtractBaseSymbol(targetSymbol);

            // Mini → Micro：手数放大
            if (_pairsByMini.TryGetValue(sourceBase, out var pair1))
            {
                if (pair1.MicroSymbol.Equals(targetBase, StringComparison.OrdinalIgnoreCase))
                {
                    return pair1.QuantityRatio;
                }
            }

            // Micro → Mini：手数缩小
            if (_pairsByMicro.TryGetValue(sourceBase, out var pair2))
            {
                if (pair2.MiniSymbol.Equals(targetBase, StringComparison.OrdinalIgnoreCase))
                {
                    return 1.0 / pair2.QuantityRatio;
                }
            }

            return 1.0;
        }

        /// <summary>
        /// 计算跨合约转换后的手数
        /// </summary>
        public int ConvertQuantity(int sourceQuantity, string sourceSymbol, string targetSymbol)
        {
            double ratio = GetQuantityRatio(sourceSymbol, targetSymbol);
            double converted = sourceQuantity * ratio;
            int result = (int)Math.Round(converted);
            return result < 1 ? 1 : result;
        }

        /// <summary>
        /// 获取合约对信息
        /// </summary>
        public CrossOrderPair GetPair(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return null;

            string baseSymbol = ExtractBaseSymbol(symbol);

            if (_pairsByMini.TryGetValue(baseSymbol, out var pair1))
                return pair1;

            if (_pairsByMicro.TryGetValue(baseSymbol, out var pair2))
                return pair2;

            return null;
        }

        /// <summary>
        /// 获取对应的转换目标 Symbol
        /// </summary>
        public string GetConversionTarget(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return null;

            string baseSymbol = ExtractBaseSymbol(symbol);

            if (_pairsByMini.TryGetValue(baseSymbol, out var pair1))
                return pair1.MicroSymbol;

            if (_pairsByMicro.TryGetValue(baseSymbol, out var pair2))
                return pair2.MiniSymbol;

            return null;
        }

        /// <summary>
        /// 获取所有合约对
        /// </summary>
        public List<CrossOrderPair> GetAllPairs()
        {
            return new List<CrossOrderPair>(_allPairs);
        }

        /// <summary>
        /// 添加自定义合约对
        /// </summary>
        public void AddCustomPair(string miniSymbol, string microSymbol, double quantityRatio, string description = "")
        {
            if (string.IsNullOrEmpty(miniSymbol) || string.IsNullOrEmpty(microSymbol))
                return;

            // 移除已存在的同名对
            RemovePair(miniSymbol);

            AddPair(miniSymbol, microSymbol, quantityRatio, description);
        }

        /// <summary>
        /// 移除合约对
        /// </summary>
        public void RemovePair(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return;

            var pair = GetPair(symbol);
            if (pair != null)
            {
                _allPairs.Remove(pair);
                _pairsByMini.Remove(pair.MiniSymbol);
                _pairsByMicro.Remove(pair.MicroSymbol);
            }
        }

        /// <summary>
        /// 判断是否为 Mini 合约
        /// </summary>
        public bool IsMiniContract(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return false;

            string baseSymbol = ExtractBaseSymbol(symbol);
            return _pairsByMini.ContainsKey(baseSymbol);
        }

        /// <summary>
        /// 判断是否为 Micro 合约
        /// </summary>
        public bool IsMicroContract(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return false;

            string baseSymbol = ExtractBaseSymbol(symbol);
            return _pairsByMicro.ContainsKey(baseSymbol);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 提取基础 Symbol（去掉月份后缀）
        /// 例如："NQ 03-26" → "NQ", "MES 12-25" → "MES"
        /// </summary>
        private string ExtractBaseSymbol(string fullSymbol)
        {
            if (string.IsNullOrEmpty(fullSymbol))
                return fullSymbol;

            // 按空格分割，取第一部分
            int spaceIndex = fullSymbol.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return fullSymbol.Substring(0, spaceIndex);
            }

            return fullSymbol;
        }

        #endregion
    }

    /// <summary>
    /// 跨合约对配置
    /// </summary>
    public class CrossOrderPair
    {
        /// <summary>
        /// Mini 合约 Symbol（如 ES, NQ）
        /// </summary>
        public string MiniSymbol { get; set; }

        /// <summary>
        /// Micro 合约 Symbol（如 MES, MNQ）
        /// </summary>
        public string MicroSymbol { get; set; }

        /// <summary>
        /// 手数转换比例（Mini:Micro）
        /// 例如 10 表示 1 手 Mini = 10 手 Micro
        /// </summary>
        public double QuantityRatio { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => $"{MiniSymbol} ↔ {MicroSymbol} ({QuantityRatio}:1)";

        public override string ToString()
        {
            return $"{MiniSymbol} ↔ {MicroSymbol} (1:{QuantityRatio}) - {Description}";
        }
    }
}
