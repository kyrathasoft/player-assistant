using System.Text.Json;

namespace PlayerAssistant
{
    internal sealed class ResourceBudgetPolicy
    {
        private readonly IReadOnlyDictionary<string, long> _budgets;
        public long BrokerQueryLatencyMilliseconds => Get("broker_query_latency_ms");
        public long MessageTableRows => Get("message_table_rows");
        public long CacheRetentionDays => Get("cache_retention_days");
        public long BackupRetentionCount => Get("backup_retention_count");
        public long StartupMilliseconds => Get("startup_ms");
        public long PwaPollingSeconds => Get("pwa_polling_seconds");
        public long OptionalPackBytes => Get("optional_pack_bytes");
        public long DiagnosticBytes => Get("diagnostic_bytes");

        private ResourceBudgetPolicy(IReadOnlyDictionary<string, long> budgets) => _budgets = budgets;

        public static ResourceBudgetPolicy Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1) throw new InvalidOperationException("Unsupported resource budget schema.");
            var values = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var property in root.GetProperty("budgets").EnumerateObject())
            {
                var value = property.Value.GetInt64();
                if (value <= 0) throw new InvalidOperationException($"Resource budget '{property.Name}' must be positive.");
                values.Add(property.Name, value);
            }
            return new ResourceBudgetPolicy(values);
        }

        public void EnsureWithin(string name, long observed)
        {
            if (!_budgets.TryGetValue(name, out var budget)) throw new InvalidOperationException($"Unknown resource budget '{name}'.");
            if (observed > budget) throw new InvalidOperationException($"Resource budget '{name}' exceeded: observed {observed}, limit {budget}.");
        }

        private long Get(string name) => _budgets.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"Missing resource budget '{name}'.");
    }
}
