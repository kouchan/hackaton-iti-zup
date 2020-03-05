using System;
using System.Text.Json;

namespace Backend.Domain.Core.Events
{
    public abstract class Event
    {
        public string Timestamp { get; }
        public string UniqueId { get; }
        public string CorrelationId { get; }
        public string Type { get; protected set; }
        public string Message { get; }
        public string Version { get; }

        protected Event(Type type, string message, int version = 1)
        {
            Timestamp = DateTime.UtcNow.ToString("O");
            UniqueId = Guid.NewGuid().ToString();
            CorrelationId = Guid.NewGuid().ToString();
            Type = type?.Name;
            Message = message;
            Version = version.ToString();
        }

        protected Event(Type type, object message, int version = 1)
        {
            Timestamp = DateTime.UtcNow.ToString("O");
            UniqueId = Guid.NewGuid().ToString();
            CorrelationId = Guid.NewGuid().ToString();
            Type = type?.Name;
            Message = JsonSerializer.Serialize(message);
            Version = version.ToString();
        }
    }
}
