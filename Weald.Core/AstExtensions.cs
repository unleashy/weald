namespace Weald.Core;

public static class AstExtensions
{
    extension(IAst self)
    {
        public void Walk(Action<IAst> f)
        {
            foreach (var child in self.Children()) {
                if (child != null) {
                    f(child);
                }
            }
        }
    }
}
