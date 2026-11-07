namespace System.Windows.Forms;

/// <summary>
/// Stand-in for the Windows Forms synchronization context. <c>ConcurrentObservableCollection&lt;T&gt;</c> detects it by
/// type name because Meziantou.Framework.Threading must not depend on a UI framework, and neither must this test project.
/// The real type is sealed, so an application cannot provide a different type with this name.
/// </summary>
internal sealed class WindowsFormsSynchronizationContext : global::System.Threading.SynchronizationContext
{
}
