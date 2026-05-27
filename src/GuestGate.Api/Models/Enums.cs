namespace GuestGate.Api.Models


{ public enum
 SessionStatus : byte
    { Active = 1, Completed = 2, Cancelled = 3, Expired = 4 }

    public enum
 ConsentStatus : byte
    { Waiting = 1, Assigned = 2, Signed = 3, Cancelled = 4 }
}