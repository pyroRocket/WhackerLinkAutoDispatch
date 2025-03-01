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

using Newtonsoft.Json;

namespace WhackerLinkAutoDispatch
{
    /// <summary>
    /// Object that represents a Imperial Call Request
    /// </summary>
    public class ImperialCallRequest
    {
        [JsonProperty(PropertyName = "commId")]
        public string CommId { get; set; }

        [JsonProperty(PropertyName = "users_discordID")]
        public string DiscordID { get; set; }

        [JsonProperty(PropertyName = "street")]
        public string Street {  get; set; }

        [JsonProperty(PropertyName = "cross_street")]
        public string CrossStreet { get; set; }

        [JsonProperty(PropertyName = "postal")]
        public string Postal { get; set; }

        [JsonProperty(PropertyName = "city")]
        public string City { get; set; }

        [JsonProperty(PropertyName = "county")]
        public string County { get; set; }

        [JsonProperty(PropertyName = "info")]
        public string Info { get; set; }

        [JsonProperty(PropertyName = "nature")]
        public string Nature { get; set; }

        [JsonProperty(PropertyName = "status")]
        public string Status { get; set; } // PENDING, ACTIVE, CLOSED

        [JsonProperty(PropertyName = "priority")]
        public int Priority { get; set; } // 1-3

        /// <summary>
        /// Creates an instance of <see cref="ImperialCallRequest"/>
        /// </summary>
        public ImperialCallRequest() { /* stub */ }
    }
}
