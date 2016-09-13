using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RestSharp.Portable
{
	public static class TypeExtensions
	{
		public static bool IsInstanceOfType(this Type type, object obj)
		{
			return obj != null && type.GetTypeInfo().IsAssignableFrom(obj.GetType().GetTypeInfo());
		}
	}
}
