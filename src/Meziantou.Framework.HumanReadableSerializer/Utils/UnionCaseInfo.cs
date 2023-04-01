using System.Reflection;

namespace Meziantou.Framework.HumanReadable.Utils;

internal sealed class UnionCaseInfo
{
    private readonly FSharpUtils _utils;
    private readonly object _inner;

    public UnionCaseInfo(FSharpUtils utils, object inner)
    {
        _utils = utils;
        _inner = inner;
    }

    public string Name
    {
        get
        {
            try
            {
                return _utils.UnionCaseInfo_NameProperty!.GetValue(_inner, index: null) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    public int Tag
    {
        get
        {
            try
            {
                if (_utils.UnionCaseInfo_TagProperty!.GetValue(_inner, index: null) is int i)
                    return i;
            }
            catch
            {
            }

            return -1;
        }
    }

    public PropertyInfo[] GetFields()
    {
        return (PropertyInfo[])_utils.UnionCaseInfo_GetFieldsMethod.Invoke(_inner, parameters: null);
    }
}
