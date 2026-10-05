#pragma warning disable CS8618
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Text.Json;
    using FastEndpoints;

    namespace SubscriberClient;

    public class EventRecord : IEventDeliveryAckStorageRecord
    {
        public long ID { get; set; }
        public string SubscriberID { get; set; } = null!;
        public Guid TrackingID { get; set; }

        [NotMapped]
        public object Event { get; set; } = null!;

        public string EventJson { get; set; } = null!;

        public TEvent GetEvent<TEvent>() where TEvent : IEvent
            => JsonSerializer.Deserialize<TEvent>(EventJson)!;

        public void SetEvent<TEvent>(TEvent @event) where TEvent : IEvent
        {
            Event = @event;
            EventJson = JsonSerializer.Serialize(@event);
        }

        public string EventType { get; set; } = null!;
        public DateTime ExpireOn { get; set; }
        public DateTime? RetainUntil { get; set; }
        public bool IsComplete { get; set; }
    }