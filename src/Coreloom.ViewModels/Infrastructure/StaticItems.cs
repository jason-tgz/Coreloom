using System.ComponentModel;

namespace Coreloom.ViewModels.Infrastructure;

/// <summary>
/// Base for immutable record types that are data-bound (option lists, spec rows). WPF binds to a plain object
/// through a PropertyDescriptor and keeps a strong reference to it in a static table for as long as the binding
/// is alive; implementing <see cref="INotifyPropertyChanged"/> (with an event that is never raised) makes WPF use
/// its weak event path instead, so bound items can always be collected when the list is rebuilt.
/// </summary>
public abstract record StaticRecord : INotifyPropertyChanged
{
    /// <summary>Never raised: the item is immutable. Accessors are empty so nothing is retained.</summary>
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }
}

/// <summary>Class counterpart of <see cref="StaticRecord"/> for immutable bound types that are not records.</summary>
public abstract class StaticItem : INotifyPropertyChanged
{
    /// <summary>Never raised: the item is immutable. Accessors are empty so nothing is retained.</summary>
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }
}
