using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mideej.Models
{
    public class ThemeOption
    {
        public string Name { get; set; } = string.Empty; // "Dark", "Light", etc.
        public string DisplayName { get; set; } = string.Empty; // What appears in the dropdown
        public string Icon { get; set; } = string.Empty; // Optional emoji/icon
    }
}
