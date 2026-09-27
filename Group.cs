using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace GroupedGridPerf
{
    public class Group : ObservableCollection<string>
    {
        public string Name { get; set; }

        public Group(string name, IEnumerable<string> items) : base(items)
        {
            Name = name;
        }
    }
}
