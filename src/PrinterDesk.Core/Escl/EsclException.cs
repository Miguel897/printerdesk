namespace PrinterDesk.Core.Escl;

/// <summary>A scanner-side problem with a message that can be shown to the user as is.</summary>
public sealed class EsclException(string message, Exception? inner = null) : Exception(message, inner);
