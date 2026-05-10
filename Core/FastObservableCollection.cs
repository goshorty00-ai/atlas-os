using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AtlasAI.Core
{
    public sealed class FastObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppress;

        public void ReplaceWith(IEnumerable<T> items)
        {
            _suppress = true;
            try
            {
                ClearItems();
                foreach (var item in items)
                    Add(item);
            }
            finally
            {
                _suppress = false;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (_suppress) return;
            base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (_suppress) return;
            base.OnPropertyChanged(e);
        }
    }
}

