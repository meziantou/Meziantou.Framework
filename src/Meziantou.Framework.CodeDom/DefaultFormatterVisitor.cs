namespace Meziantou.Framework.CodeDom;

public class DefaultFormatterVisitor : Visitor
{
    public static DefaultFormatterVisitor Instance { get; } = new DefaultFormatterVisitor();

    public override void VisitClassDeclaration(ClassDeclaration classDeclaration)
    {
        classDeclaration.Members.Sort(MemberComparer.Instance);
        base.VisitClassDeclaration(classDeclaration);
    }

    public override void VisitStructDeclaration(StructDeclaration structDeclaration)
    {
        structDeclaration.Members.Sort(MemberComparer.Instance);
        base.VisitStructDeclaration(structDeclaration);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
    {
        interfaceDeclaration.Members.Sort(MemberComparer.Instance);
        base.VisitInterfaceDeclaration(interfaceDeclaration);
    }

    private sealed class MemberComparer : IComparer<MemberDeclaration?>
    {
        public static MemberComparer Instance { get; } = new MemberComparer();

        public int Compare(MemberDeclaration? x, MemberDeclaration? y)
        {
            var sortOrderX = SortOrder(x);
            var sortOrderY = SortOrder(y);
            var result = sortOrderX.CompareTo(sortOrderY);
            if (result != 0)
                return result;

            return StringComparer.Ordinal.Compare(x?.Name, y?.Name);
        }

        private static int SortOrder(MemberDeclaration? m)
        {
            return m switch
            {
                FieldDeclaration o => 100 + GetModifiersSortOrder(o) * 10 + GetVisibilitySortOrder(o),
                EventFieldDeclaration o => 200 + GetModifiersSortOrder(o) * 10 + GetVisibilitySortOrder(o),
                ConstructorDeclaration o => 300 + GetModifiersSortOrder(o) * 10 + GetVisibilitySortOrder(o),
                PropertyDeclaration o => 400 + GetModifiersSortOrder(o) * 10 + GetVisibilitySortOrder(o),
                MethodDeclaration o => 500 + GetModifiersSortOrder(o) * 10 + GetVisibilitySortOrder(o),
                _ => int.MaxValue,
            };
        }

        private static int GetModifiersSortOrder(MemberDeclaration member)
        {
            if (member is IModifiers modifierContainer)
            {
                var modifiers = modifierContainer.Modifiers;
                if (modifiers.HasFlag(Modifiers.Const))
                {
                    return 1;
                }

                if (modifiers.HasFlag(Modifiers.Static) && modifiers.HasFlag(Modifiers.ReadOnly))
                {
                    return 2;
                }
            }

            return int.MaxValue;
        }

        private static int GetVisibilitySortOrder(MemberDeclaration member)
        {
            if (member is IModifiers modifierContainer)
            {
                var modifiers = modifierContainer.Modifiers;
                if (modifiers.HasFlag(Modifiers.Public))
                    return 1;

                if (modifiers.HasFlag(Modifiers.Internal))
                    return 2;

                if (modifiers.HasFlag(Modifiers.Internal))
                    return 3;

                if (modifiers.HasFlag(Modifiers.Protected))
                    return 3;

                if (modifiers.HasFlag(Modifiers.Private))
                    return 4;
            }

            return int.MaxValue;
        }
    }
}
