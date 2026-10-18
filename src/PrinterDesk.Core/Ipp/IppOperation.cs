namespace PrinterDesk.Core.Ipp;

public enum IppOperation : ushort
{
    PrintJob = 0x0002,
    ValidateJob = 0x0004,
    CreateJob = 0x0005,
    SendDocument = 0x0006,
    CancelJob = 0x0008,
    GetJobAttributes = 0x0009,
    GetPrinterAttributes = 0x000B,
}

public sealed class IppException(string message, Exception? inner = null) : Exception(message, inner);
