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

using WhackerLinkLib.Models;

namespace WhackerLinkAutoDispatch
{

    /// <summary>
    /// Config template object
    /// </summary>
    public class DispatchTemplate
    {
        public string TemplateName { get; set; }
        public string MurfApiKey { get; set; }
        public bool Repeat { get; set; } = false;
        public NetworkConfig Network { get; set; }
        public List<Channel> Channels { get; set; }
        public TtsConfig TtsConfig { get; set; }
        public DvmConfig Dvm { get; set; } = null;
        public ImperialConfig Imperial { get; set; }
        public List<Field> Fields { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class DvmConfig
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 34001;
        public string Address { get; set; } = "127.0.0.1";
    }

    /// <summary>
    /// 
    /// </summary>
    public class Channel
    {
        public string Name { get; set; }
        public string DstId { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class NetworkConfig
    {
        public string Address { get; set; }
        public int Port { get; set; }
        public Site Site { get; set; }
        public string SrcId { get; set; } = "1";
    }

    /// <summary>
    /// 
    /// </summary>
    public class ImperialConfig
    {
        public bool Enabled { get; set; } = false;
        public string CommId { get; set; }
        public string ApiKey { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class TtsConfig
    {
        public int Rate { get; set; } = -8;
        public int Pitch { get; set; } = -8;
    }
}
