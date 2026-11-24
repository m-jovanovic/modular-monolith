namespace EquipmentLibrary.AnalyzerGuard;

public static class StackTraceGuard
{
    public static void Invoke(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            operation();
        }
        catch (InvalidOperationException exception)
        {
            // The fixture intentionally resets the original stack trace to exercise CA2200.
            throw exception;
        }
    }
}
