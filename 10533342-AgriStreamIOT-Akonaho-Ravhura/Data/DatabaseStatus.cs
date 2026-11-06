namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    public enum DatabaseState
    {
        Pending,
        Ready,
        Failed
    }

    /// <summary>
    /// Thread-safe snapshot of the background database initialisation, exposed on /health.
    /// Holds only a short, safe summary: never connection strings or full exception text.
    /// </summary>
    public class DatabaseStatus
    {
        private readonly object _lock = new();

        public DatabaseState State { get; private set; } = DatabaseState.Pending;
        public int Attempts { get; private set; }
        public string? Message { get; private set; }
        public DateTimeOffset? LastUpdatedUtc { get; private set; }

        public void SetAttempt(int attempt)
        {
            lock (_lock)
            {
                State = DatabaseState.Pending;
                Attempts = attempt;
                Message = $"Initialising database (attempt {attempt}).";
                LastUpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        public void SetReady()
        {
            lock (_lock)
            {
                State = DatabaseState.Ready;
                Message = "Database migrated and seeded.";
                LastUpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        public void SetFailed(string safeMessage)
        {
            lock (_lock)
            {
                State = DatabaseState.Failed;
                Message = safeMessage;
                LastUpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        public object ToHealthPayload()
        {
            lock (_lock)
            {
                return new
                {
                    status = State == DatabaseState.Failed ? "Degraded" : "Healthy",
                    database = State.ToString(),
                    attempts = Attempts,
                    message = Message,
                    lastUpdatedUtc = LastUpdatedUtc
                };
            }
        }
    }
}
