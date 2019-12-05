using System;
using System.Runtime.Serialization;

namespace DataManagement
{
    [Serializable]
    public class MissingParameterException : Exception
    {
        public MissingParameterException(string message) : base(message)
        {
        }

        protected MissingParameterException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}