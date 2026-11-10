using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.AI.Actions;
using Windows.AI.Actions.Provider;
using Windows.Foundation;

namespace Mizon.Windows.Services;

// No input entities: Microsoft.AI.Actions 0.1.0's method generator requires an
// input. Use its supported manual-provider path and generated COM class factory.
[Guid(AppActionCatalog.ProviderClsid)]
internal sealed partial class MizonActionProvider : IActionProvider
{
    public IAsyncAction InvokeAsync(ActionInvocationContext context) =>
        global::System.Runtime.InteropServices.WindowsRuntime.AsyncInfo.Run(async cancellationToken =>
        {
            context.Result = await Program.InvokeAppActionAsync(context.ActionId, cancellationToken)
                ? ActionInvocationResult.Success
                : ActionInvocationResult.Unavailable;
        });
}
