using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CompInfo
{
    // Office network profiles from the cloud (web page «Сеть»), cached in network-profiles.json (lan-profiles.ps1).
    public class NetworkProfiles
    {
        public class Location
        {
            public string Name;
            public int ThirdOctet, PrefixLength, GatewayOctet, FirstHostOctet, LastHostOctet;
            public string SubnetMask;
            public List<string> DnsServers = new List<string>();
            public List<int> Segments;          // own segments, or null for the common list
        }

        public bool Missing = true;
        public List<Location> Locations = new List<Location>();
        public List<int> Segments = new List<int>();
        public int FirstOctet = 10;
        public string DomainCheckHost;
        // Domain computers are joined to: «Домен», or else the domain check host; null when neither is set.
        public string DomainName;

        public static string MaskFromPrefix(int prefixLength)
        {
            uint mask = 0;
            for (int bit = 0; bit < prefixLength; bit++) mask |= 1u << (31 - bit);
            return string.Format("{0}.{1}.{2}.{3}", (mask >> 24) & 255, (mask >> 16) & 255, (mask >> 8) & 255, mask & 255);
        }

        static List<int> Ints(object value)
        {
            List<int> result = new List<int>();
            System.Collections.IList list = value as System.Collections.IList;
            if (list != null) foreach (object item in list) result.Add(Convert.ToInt32(item));
            return result;
        }

        static int Int(JsonObject o, string name) { return o[name] == null ? 0 : Convert.ToInt32(o[name]); }

        public static NetworkProfiles Read(string path)
        {
            NetworkProfiles empty = new NetworkProfiles();
            if (!File.Exists(path)) return empty;
            try
            {
                JsonObject value = Files.ReadRecord(path)["value"] as JsonObject;
                if (value == null) return empty;
                NetworkProfiles p = new NetworkProfiles();
                System.Collections.IList locations = value["locations"] as System.Collections.IList;
                if (locations != null)
                {
                    foreach (object item in locations)
                    {
                        JsonObject o = item as JsonObject;
                        if (o == null) continue;
                        Location l = new Location();
                        l.Name = o.Text("name") ?? "";
                        l.ThirdOctet = Int(o, "thirdOctet");
                        l.PrefixLength = Int(o, "prefixLength");
                        l.SubnetMask = MaskFromPrefix(l.PrefixLength);
                        l.GatewayOctet = Int(o, "gatewayOctet");
                        l.FirstHostOctet = Int(o, "firstHostOctet");
                        l.LastHostOctet = Int(o, "lastHostOctet");
                        System.Collections.IList dns = o["dnsServers"] as System.Collections.IList;
                        if (dns != null) foreach (object d in dns) l.DnsServers.Add(Convert.ToString(d));
                        List<int> own = Ints(o["segments"]);
                        l.Segments = own.Count > 0 ? own : null;
                        p.Locations.Add(l);
                    }
                }
                if (p.Locations.Count == 0) return empty;
                p.Missing = false;
                p.Segments = Ints(value["segments"]);
                p.FirstOctet = value["firstOctet"] == null ? 10 : Convert.ToInt32(value["firstOctet"]);
                string host = value.Text("domainCheckHost");
                p.DomainCheckHost = string.IsNullOrEmpty(host) ? null : host;
                string domain = (value.Text("domainName") ?? p.DomainCheckHost ?? "").Trim().ToLowerInvariant();
                p.DomainName = System.Text.RegularExpressions.Regex.IsMatch(domain, "^(?=.{1,253}$)[a-z0-9]+(?:[.-][a-z0-9]+)*\\.[a-z0-9]*[a-z][a-z0-9]*$") ? domain : null;
                return p;
            }
            catch (Exception) { return empty; }
        }

        // Address inside one of the office networks (from the gateway to the last host).
        public bool IsOfficeAddress(string address)
        {
            IPAddress parsed;
            if (!IPAddress.TryParse(address ?? "", out parsed) || parsed.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] o = parsed.GetAddressBytes();
            foreach (Location l in Locations)
            {
                List<int> segments = l.Segments ?? Segments;
                if (o[0] == FirstOctet && segments.Contains(o[1]) && o[2] == l.ThirdOctet && o[3] >= l.GatewayOctet && o[3] <= l.LastHostOctet)
                    return true;
            }
            return false;
        }
    }

    // Quick ping: own address, gateway, DNS, then domain or internet (quick-ping.ps1).
    public static class QuickPing
    {
        public class Target
        {
            public string Label;
            public string Address;
        }

        public class Plan
        {
            public string AdapterName;
            public int AdapterIndex;
            public List<Target> Targets = new List<Target>();
        }

        // Active adapter with the best default route and the addresses to check, in order.
        public static Plan Prepare(NetworkProfiles profiles)
        {
            Dictionary<long, Dictionary<string, object>> connected = new Dictionary<long, Dictionary<string, object>>();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT InterfaceIndex, InterfaceAlias, InterfaceMetric, ConnectionState FROM MSFT_NetIPInterface WHERE AddressFamily=2"))
                if (Wmi.Num(row, "ConnectionState") == 1) connected[Wmi.Num(row, "InterfaceIndex")] = row;

            long bestIndex = -1, bestMetric = long.MaxValue;
            string gateway = null;
            foreach (Dictionary<string, object> route in Wmi.Query(Wmi.Net, "SELECT InterfaceIndex, NextHop, RouteMetric FROM MSFT_NetRoute WHERE AddressFamily=2 AND DestinationPrefix='0.0.0.0/0'"))
            {
                long index = Wmi.Num(route, "InterfaceIndex");
                Dictionary<string, object> ipInterface;
                if (!connected.TryGetValue(index, out ipInterface)) continue;
                long metric = Wmi.Num(route, "RouteMetric") + Wmi.Num(ipInterface, "InterfaceMetric");
                if (metric < bestMetric) { bestMetric = metric; bestIndex = index; gateway = Wmi.Str(route, "NextHop"); }
            }
            if (bestIndex < 0)
            {
                foreach (KeyValuePair<long, Dictionary<string, object>> p in connected)
                {
                    long metric = Wmi.Num(p.Value, "InterfaceMetric");
                    if (metric < bestMetric) { bestMetric = metric; bestIndex = p.Key; }
                }
            }
            if (bestIndex < 0) throw new InvalidOperationException("Не найдено активное IPv4-подключение.");

            Plan plan = new Plan();
            plan.AdapterIndex = (int)bestIndex;
            plan.AdapterName = Wmi.Str(connected[bestIndex], "InterfaceAlias") ?? "";
            string local = null;
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT IPAddress, AddressState, SkipAsSource FROM MSFT_NetIPAddress WHERE AddressFamily=2 AND InterfaceIndex=" + bestIndex))
            {
                if (Wmi.Num(row, "AddressState") == 4 && !Wmi.Bool(row, "SkipAsSource")) { local = Wmi.Str(row, "IPAddress"); break; }
            }
            List<string> dns = new List<string>();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT ServerAddresses FROM MSFT_DNSClientServerAddress WHERE AddressFamily=2 AND InterfaceIndex=" + bestIndex))
                foreach (string s in Wmi.Strings(row, "ServerAddresses"))
                    if (!string.IsNullOrWhiteSpace(s) && !dns.Contains(s)) dns.Add(s);

            Add(plan, "Свой IP", local);
            Add(plan, "Основной шлюз", gateway);
            if (dns.Count == 0) Add(plan, "DNS", null);
            for (int i = 0; i < dns.Count; i++) Add(plan, "DNS " + (i + 1), dns[i]);
            // The domain host comes from the cloud network profiles (cached by synchronization).
            if (profiles.DomainCheckHost != null && profiles.IsOfficeAddress(local)) Add(plan, "Домен", profiles.DomainCheckHost);
            else Add(plan, "Интернет", "1.1.1.1");
            return plan;
        }

        static void Add(Plan plan, string label, string address)
        {
            Target t = new Target();
            t.Label = label;
            t.Address = address;
            plan.Targets.Add(t);
        }

        // One request, up to 2 seconds. Returns null on success, otherwise the reason.
        public static string Ping(string address, out string details)
        {
            details = "";
            if (string.IsNullOrWhiteSpace(address)) return "адрес не настроен";
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(address, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        details = string.Format("{0}, {1} мс, TTL={2}", reply.Address, reply.RoundtripTime, reply.Options != null ? reply.Options.Ttl : 0);
                        return null;
                    }
                    return StatusText(reply.Status);
                }
            }
            catch (Exception e) { return e.GetBaseException().Message; }
        }

        static string StatusText(IPStatus status)
        {
            switch (status)
            {
                case IPStatus.TimedOut: return "нет ответа (истекло время ожидания)";
                case IPStatus.DestinationHostUnreachable: return "узел недоступен";
                case IPStatus.DestinationNetworkUnreachable: return "сеть недоступна";
                default: return "нет ответа (" + status + ")";
            }
        }
    }
}
