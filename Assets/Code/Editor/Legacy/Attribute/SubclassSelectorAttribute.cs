// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class SubclassSelectorAttribute : PropertyAttribute
{
	bool m_includeMono;

	public SubclassSelectorAttribute(bool includeMono = false)
	{
		m_includeMono = includeMono;
	}

	public bool IsIncludeMono()
	{
		return m_includeMono;
	}
}
#endif
