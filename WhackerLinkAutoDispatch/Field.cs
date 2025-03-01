/*
* WhackerLink - WhackerLink Auto Dispatch
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
*
* You should have received a copy of the GNU General Public License
* along with this program.  If not, see <http://www.gnu.org/licenses/>.
* 
* Copyright (C) 2025 Caleb, K4PHP
* 
*/

namespace WhackerLinkAutoDispatch
{
    /// <summary>
    /// 
    /// </summary>
    public class Field
    {
        public string Name { get; set; }
        public string SaidName { get; set; }
        public string Type { get; set; }
        public string Separator { get; set; } = ", ";
        public string Ender { get; set; } = "";
        public bool IncludeFieldName { get; set; } = false;
        public bool Multiple { get; set; } = false;
        public bool NoRepeat { get; set; } = false;
        public bool EndOnly { get; set; } = false;
        public bool IsImperialStreet { get; set; } = false;
        public bool IsImperialPostal { get; set; } = false;
        public bool IsImperialNature { get; set; } = false;
        public bool IsImperialNote { get; set; } = false;
        public List<string> Options { get; set; }
    }
}
