using System.Collections;
using System.Text;

namespace Weald.Core;

public sealed class Source : IEnumerable<char>
{
    public string Name { get; }
    public string Body { get; }

    [Pure]
    public static Source FromString(string name, string body)
    {
        return new Source(name, body);
    }

    public static Source FromFile(string path)
    {
        var body = File.ReadAllText(path, Encoding.UTF8);
        return new Source(path, body);
    }

    private Source(string name, string body)
    {
        Name = name;
        Body = body;
    }

    public char this[int index] => Body[index];

    public int Length => Body.Length;

    [Pure]
    public IEnumerator<char> GetEnumerator() => Body.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [Pure]
    public override string ToString()
    {
        var truncBody = Body.Length > 25 ? Body[0..25] + "<...>" : Body;
        return $"Source {{ Name = {Name}, Body = {truncBody.Escape()} }}";
    }
}
