namespace MailMerge.Api.Features.Campaigns.Delivery;

/// <summary>Wakes the worker early when a campaign starts, so "Send" doesn't wait for the next poll.</summary>
public sealed class DeliverySignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled.
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}
