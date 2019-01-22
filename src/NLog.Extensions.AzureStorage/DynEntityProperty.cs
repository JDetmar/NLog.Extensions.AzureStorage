using System;
using NLog.Config;
using NLog.Layouts;

namespace NLog.Extensions.AzureStorage
{
    [NLogConfigurationItem]
    public class DynEntityProperty
    {
        [RequiredParameter]
        public string Name { get; set; }

        [RequiredParameter]
        public Layout Layout { get; set; }

        public override string ToString()
        {
            return $"Name: {Name}, Layout: {Layout}";
        }
    }
}
