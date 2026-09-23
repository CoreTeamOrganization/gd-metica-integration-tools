using System;
using System.Collections.Generic;

namespace GameDistrict.MeticaIntegrationTools
{
    [Serializable]
    internal class GenreEventDef
    {
        public string              Name;
        public List<GenreFieldDef> Fields;

        public GenreEventDef(string name, List<GenreFieldDef> fields)
        {
            Name   = name;
            Fields = fields;
        }
    }

    [Serializable]
    internal class GenreFieldDef
    {
        public string        Name;
        public GenreFieldType Type;
        // When set, used as the analytics payload key instead of Name (preserves Excel/user input verbatim).
        public string        PayloadKey;

        public GenreFieldDef(string name, GenreFieldType type, string payloadKey = null)
        {
            Name       = name;
            Type       = type;
            PayloadKey = payloadKey;
        }
    }

    internal enum GenreFieldType { String, Int, Float, Bool }
}