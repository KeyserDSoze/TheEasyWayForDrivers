using System.Runtime.InteropServices;

namespace TheEasyWayForDrivers.ServiceApp.Services;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class WuaAutomationCallback
{
    [DispId(0)]
    public void Invoke(object? job, object? callbackArgs)
    {
    }
}
