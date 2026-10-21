using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// Builds the collation table of a CLDR tailoring (UTS 35 part 5): the semantics of ICU's CollationBuilder. The rules
/// insert nodes in lists of weights that start at the root primaries; each node has a strength and is either a root
/// weight or a tailored one. Once every rule is applied, the tailored nodes get weights in the gaps between the root
/// weights, and the mappings (which reference the nodes with temporary CEs) get their final collation elements.
/// </summary>
internal sealed class CollationBuilder
{
    private const int PrimaryLevel = 0;
    private const int SecondaryLevel = 1;
    private const int TertiaryLevel = 2;
    private const int QuaternaryLevel = 3;
    private const int IdenticalLevel = 15;
    private const int MaxExpansionLength = 31;

    private const ulong TempMarker = 0xFFFFFFFF00000000;

    private readonly RootCollation _root = RootCollation.Instance;
    private readonly CollationMap _map = new();
    private readonly HashSet<int> _suppressedContractions = [];
    private readonly List<Node> _nodes = [];
    private readonly List<uint> _rootPrimaries = [];
    private readonly List<int> _rootPrimaryIndexes = [];
    private readonly ulong[] _ces = new ulong[MaxExpansionLength + 1];
    private int _cesLength;
    private ulong[] _buffer = new ulong[64];
    private CollationTable _table = null!;

    [StructLayout(LayoutKind.Auto)]
    private struct Node
    {
        public uint Weight;
        public int Strength;
        public bool IsTailored;
        public bool HasBefore2;
        public bool HasBefore3;
        public int Previous;
        public int Next;
    }

    /// <summary>Builds the table of a tailoring.</summary>
    public static CollationTable Build(CollationTailoring tailoring)
    {
        var builder = new CollationBuilder();
        return builder.BuildTable(tailoring);
    }

    private CollationTable BuildTable(CollationTailoring tailoring)
    {
        foreach (var set in tailoring.SuppressContractions)
        {
            foreach (var codePoint in ParseUnicodeSet(set))
            {
                _suppressedContractions.Add(codePoint);
            }
        }

        _table = new CollationTable(_map, _suppressedContractions);
        _nodes.Add(new Node { Weight = 0, Strength = PrimaryLevel });
        _rootPrimaries.Add(0);
        _rootPrimaryIndexes.Add(0);

        foreach (var chain in tailoring.Chains)
        {
            AddReset(chain.Before is { } before ? ToLevel(before) : IdenticalLevel, chain.Reset, chain.ResetPosition);
            foreach (var relation in chain.Relations)
            {
                AddRelation(ToLevel(relation.Strength), relation.Prefix, relation.Text, relation.Extension);
            }
        }

        var nodeCEs = MakeTailoredCEs();
        _map.TransformElements(ce => IsTempCE(ce) ? nodeCEs[IndexFromTempCE(ce)] | (ce & CollationWeights.CaseMask) : ce);
        _map.Trim();
        return _table;
    }

    private static int ToLevel(CollationStrength strength) => strength switch
    {
        CollationStrength.Primary => PrimaryLevel,
        CollationStrength.Secondary => SecondaryLevel,
        CollationStrength.Tertiary => TertiaryLevel,
        CollationStrength.Quaternary => QuaternaryLevel,
        _ => IdenticalLevel,
    };

    // Temporary CEs: a reference to a node, with its strength and the case bits
    private static bool IsTempCE(ulong ce) => (ce & TempMarker) == TempMarker;

    private static ulong TempCEFromIndexAndStrength(int index, int strength) => TempMarker | ((ulong)(index & 0xFFFF) << 16) | ((ulong)(index >> 16) << 3) | (uint)strength;

    private static int IndexFromTempCE(ulong ce) => (int)((ce >> 16) & 0xFFFF) | ((int)((ce >> 3) & 0x7FF) << 16);

    private static int StrengthFromTempCE(ulong ce) => (int)(ce & 7);

    private static int CeStrength(ulong ce)
    {
        if (IsTempCE(ce))
            return StrengthFromTempCE(ce);

        if (CollationWeights.Primary(ce) != 0)
            return PrimaryLevel;

        if (CollationWeights.Secondary(ce) != 0)
            return SecondaryLevel;

        return ce != 0 ? TertiaryLevel : IdenticalLevel;
    }

    private int[] Decompose(string text) => _root.Decompose(text);

    private int GetCEs(int[] nfd, int start)
    {
        var count = _table.GetElements(nfd.AsSpan(), ref _buffer, 0, numeric: false);
        if (start + count > MaxExpansionLength)
            throw new FormatException("Too many collation elements for a tailored string");

        _buffer.AsSpan(0, count).CopyTo(_ces.AsSpan(start));
        return start + count;
    }

    private void AddReset(int strength, string text, string? position)
    {
        if (position is not null)
        {
            _ces[0] = GetSpecialResetPosition(position);
            _cesLength = 1;
        }
        else
        {
            var nfd = Decompose(text);
            if (nfd.Length == 2 && nfd[0] == 0xFDD1)
            {
                // ICU: U+FDD1 followed by a character is the first primary of the script group of the character
                _ces[0] = CollationWeights.MakeCE(_root.GetGroupFirstPrimary(nfd[1]));
                _cesLength = 1;
            }
            else
            {
                _cesLength = GetCEs(nfd, 0);
            }
        }

        if (strength == IdenticalLevel)
            return;

        // &[before strength]position
        var index = FindOrInsertNodeForCEs(strength);
        var node = _nodes[index];

        // If the index is for a "weaker" node, then skip backwards over this and further "weaker" nodes
        while (node.Strength > strength)
        {
            index = node.Previous;
            node = _nodes[index];
        }

        if (node.Strength == strength && node.IsTailored)
        {
            // Reset to just before this same-strength tailored node
            index = node.Previous;
        }
        else if (strength == PrimaryLevel)
        {
            // root primary node (has no previous index)
            var p = node.Weight;
            if (p == 0)
                throw new FormatException("Reset primary-before ignorable is not possible");

            p = _root.GetPrimaryBefore(p);
            index = FindOrInsertNodeForPrimary(p);

            // Tailor after the last node between adjacent root nodes
            while (_nodes[index].Next != 0)
            {
                index = _nodes[index].Next;
            }
        }
        else
        {
            // &[before 2] or &[before 3]
            index = FindCommonNode(index, SecondaryLevel);
            if (strength >= TertiaryLevel)
            {
                index = FindCommonNode(index, TertiaryLevel);
            }

            node = _nodes[index];
            if (node.Strength == strength)
            {
                // Found a same-strength node with an explicit weight
                var weight16 = node.Weight;
                if (weight16 == 0)
                    throw new FormatException("Reset secondary/tertiary-before completely ignorable is not possible");

                // Reset to just before this node: insert the preceding same-level explicit weight if it is not there already
                weight16 = GetWeight16Before(node, strength);
                uint previousWeight16;
                var previousIndex = node.Previous;
                for (var i = previousIndex; ; i = node.Previous)
                {
                    node = _nodes[i];
                    var previousStrength = node.Strength;
                    if (previousStrength < strength)
                    {
                        previousWeight16 = CollationWeights.CommonWeight16;
                        break;
                    }

                    if (previousStrength == strength && !node.IsTailored)
                    {
                        previousWeight16 = node.Weight;
                        break;
                    }
                }

                if (previousWeight16 == weight16)
                {
                    // The preceding weight has a node, maybe with following weaker or tailored nodes: reset to the last of them
                    index = previousIndex;
                }
                else
                {
                    index = InsertNodeBetween(previousIndex, index, new Node { Weight = weight16, Strength = strength });
                }
            }
            else
            {
                // Found a stronger node with implied strength-common weight
                index = FindOrInsertWeakerNode(index, CollationWeights.BeforeWeight16, strength);
            }

            // Strength of the temporary CE = strength of its reset position
            strength = CeStrength(_ces[_cesLength - 1]);
        }

        _ces[_cesLength - 1] = TempCEFromIndexAndStrength(index, strength);
    }

    private uint GetWeight16Before(Node node, int level)
    {
        // Collect the root CE weights if this node is for a root CE; if it is not, then return the low non-primary boundary for a tailored CE
        var t = node.Strength == TertiaryLevel ? node.Weight : CollationWeights.CommonWeight16;
        while (node.Strength > SecondaryLevel)
        {
            node = _nodes[node.Previous];
        }

        if (node.IsTailored)
            return CollationWeights.BeforeWeight16;

        var s = node.Strength == SecondaryLevel ? node.Weight : CollationWeights.CommonWeight16;
        while (node.Strength > PrimaryLevel)
        {
            node = _nodes[node.Previous];
        }

        if (node.IsTailored)
            return CollationWeights.BeforeWeight16;

        // [p, s, t] is a root CE: the preceding weight for the requested level
        var p = node.Weight;
        return level == SecondaryLevel ? _root.GetSecondaryBefore(p, s) : _root.GetTertiaryBefore(p, s, t);
    }

    private ulong GetSpecialResetPosition(string position)
    {
        ulong ce;
        var strength = PrimaryLevel;
        var isBoundary = false;
        var isFirst = true;
        switch (position)
        {
            case "first tertiary ignorable":
            case "last tertiary ignorable":
                return 0;
            case "first secondary ignorable":
            {
                // Look for a tailored tertiary node after [0, 0, 0]
                var index = FindOrInsertNodeForRootCE(0, TertiaryLevel);
                var next = _nodes[index].Next;
                if (next != 0 && _nodes[next] is { IsTailored: true, Strength: TertiaryLevel })
                    return TempCEFromIndexAndStrength(next, TertiaryLevel);

                return 0;
            }

            case "last secondary ignorable":
                ce = 0;
                strength = TertiaryLevel;
                isFirst = false;
                break;
            case "first primary ignorable":
            {
                // Look for a tailored secondary node after [0, 0, *]
                var index = FindOrInsertNodeForRootCE(0, SecondaryLevel);
                var node = _nodes[index];
                while ((index = node.Next) != 0)
                {
                    node = _nodes[index];
                    strength = node.Strength;
                    if (strength < SecondaryLevel)
                        break;

                    if (strength == SecondaryLevel)
                    {
                        if (node.IsTailored)
                        {
                            if (node.HasBefore3)
                            {
                                index = _nodes[_nodes[node.Next].Next].Next;
                            }

                            return TempCEFromIndexAndStrength(index, SecondaryLevel);
                        }

                        break;
                    }
                }

                ce = _root.FirstSecondaryCE;
                strength = SecondaryLevel;
                break;
            }

            case "last primary ignorable":
                ce = _root.LastSecondaryCE;
                strength = SecondaryLevel;
                isFirst = false;
                break;
            case "first variable":
                ce = CollationWeights.MakeCE(_root.FirstVariablePrimary);
                break;
            case "last variable":
                ce = _root.LastVariableCE;
                isFirst = false;
                break;
            case "first regular":
                ce = _root.FirstRegularCE;
                break;
            case "last regular":
                // ICU: the first primary of the Han group rather than the actual last "regular" CE before it
                ce = CollationWeights.MakeCE(RootCollation.HanStartPrimary);
                isFirst = false;
                break;
            case "first implicit":
                ce = _root.GetImplicitCE(0x4E00);
                break;
            case "first trailing":
                ce = CollationWeights.MakeCE(0xFFFD0000);
                isBoundary = true;
                break;
            default:
                throw new FormatException("Unsupported reset position [" + position + "]");
        }

        {
            var index = FindOrInsertNodeForRootCE(ce, strength);
            var node = _nodes[index];
            if (isFirst)
            {
                if (!node.HasBefore2 && !node.HasBefore3 && isBoundary)
                {
                    var next = node.Next;
                    if (next != 0)
                    {
                        ce = TempCEFromIndexAndStrength(next, strength);
                    }
                }

                if (node.HasBefore2 || node.HasBefore3)
                {
                    // Get the first node that was tailored before this one at a weaker strength
                    if (node.HasBefore2)
                    {
                        index = _nodes[_nodes[index].Next].Next;
                        node = _nodes[index];
                    }

                    if (node.HasBefore3)
                    {
                        index = _nodes[_nodes[index].Next].Next;
                    }

                    ce = TempCEFromIndexAndStrength(index, strength);
                }
            }
            else
            {
                // Find the last node that was tailored after the [last xyz] at a strength no greater than the position's strength
                while (true)
                {
                    var next = node.Next;
                    if (next == 0)
                        break;

                    var nextNode = _nodes[next];
                    if (nextNode.Strength < strength)
                        break;

                    index = next;
                    node = nextNode;
                }

                // Do not make a temporary CE for a root node: this last node might be the node for the root CE itself
                if (node.IsTailored)
                {
                    ce = TempCEFromIndexAndStrength(index, strength);
                }
            }
        }

        return ce;
    }

    private void AddRelation(int strength, string prefix, string text, string extension)
    {
        var nfdPrefix = prefix.Length > 0 ? Decompose(prefix) : [];
        var nfdString = Decompose(text);
        if (strength != IdenticalLevel)
        {
            // Find the node index after which we insert the new tailored node
            var index = FindOrInsertNodeForCEs(strength);
            var ce = _ces[_cesLength - 1];
            if (strength == PrimaryLevel && !IsTempCE(ce) && CollationWeights.Primary(ce) == 0)
                throw new FormatException("Tailoring a primary after ignorables is not supported");

            if (strength == QuaternaryLevel && ce == 0)
                throw new FormatException("Tailoring a quaternary after tertiary ignorables is not supported");

            index = InsertTailoredNodeAfter(index, strength);

            // The new relation may yield a stronger CE but not a weaker one
            var tempStrength = Math.Min(CeStrength(ce), strength);
            _ces[_cesLength - 1] = TempCEFromIndexAndStrength(index, tempStrength);
        }

        SetCaseBits(nfdString);
        var cesLengthBeforeExtension = _cesLength;
        if (extension.Length > 0)
        {
            _cesLength = GetCEs(Decompose(extension), _cesLength);
        }

        var elements = _ces.AsSpan(0, _cesLength);
        if (nfdPrefix.Length > 0)
        {
            _map.SetPrefixMapping(nfdPrefix, nfdString, elements);
        }
        else if (nfdString.Length == 1)
        {
            _map.SetMapping(nfdString[0], elements);
        }
        else
        {
            _map.SetContraction(nfdString, elements);
        }

        _cesLength = cesLengthBeforeExtension;
    }

    private void SetCaseBits(int[] nfdString)
    {
        var numTailoredPrimaries = 0;
        for (var i = 0; i < _cesLength; i++)
        {
            if (CeStrength(_ces[i]) == PrimaryLevel)
            {
                numTailoredPrimaries++;
            }
        }

        ulong cases = 0;
        if (numTailoredPrimaries > 0)
        {
            var rootTable = new CollationTable(tailored: null, suppressedContractions: null);
            var baseCEs = new ulong[16];
            var baseCEsLength = rootTable.GetElements(nfdString.AsSpan().ToArray(), ref baseCEs, 0, numeric: false);
            ulong lastCase = 0;
            var numBasePrimaries = 0;
            for (var i = 0; i < baseCEsLength; i++)
            {
                var ce = baseCEs[i];
                if (CollationWeights.Primary(ce) != 0)
                {
                    numBasePrimaries++;
                    var c = (ce >> 14) & 3;
                    if (numBasePrimaries < numTailoredPrimaries)
                    {
                        cases |= c << ((numBasePrimaries - 1) * 2);
                    }
                    else if (numBasePrimaries == numTailoredPrimaries)
                    {
                        lastCase = c;
                    }
                    else if (c != lastCase)
                    {
                        // There are more base primary CEs than tailored primaries: mixed case if the case bits of the remainder differ
                        lastCase = 1;
                        break;
                    }
                }
            }

            if (numBasePrimaries >= numTailoredPrimaries)
            {
                cases |= lastCase << ((numTailoredPrimaries - 1) * 2);
            }
        }

        for (var i = 0; i < _cesLength; i++)
        {
            var ce = _ces[i] & ~CollationWeights.CaseMask;
            var strength = CeStrength(ce);
            if (strength == PrimaryLevel)
            {
                ce |= (cases & 3) << 14;
                cases >>= 2;
            }
            else if (strength == TertiaryLevel)
            {
                // Tertiary CEs must have uppercase bits
                ce |= CollationWeights.UpperCase;
            }

            _ces[i] = ce;
        }
    }

    private int FindOrInsertNodeForCEs(int strength)
    {
        // Find the last CE that is at least as "strong" as the requested difference
        ulong ce;
        for (; ; _cesLength--)
        {
            if (_cesLength == 0)
            {
                ce = _ces[0] = 0;
                _cesLength = 1;
                break;
            }

            ce = _ces[_cesLength - 1];
            if (CeStrength(ce) <= strength)
                break;
        }

        if (IsTempCE(ce))
            return IndexFromTempCE(ce);

        return FindOrInsertNodeForRootCE(ce, strength);
    }

    private int FindOrInsertNodeForRootCE(ulong ce, int strength)
    {
        // Find or insert the node for each of the root CE's weights, down to the requested level/strength
        var index = FindOrInsertNodeForPrimary(CollationWeights.Primary(ce));
        if (strength >= SecondaryLevel)
        {
            index = FindOrInsertWeakerNode(index, CollationWeights.Secondary(ce), SecondaryLevel);
            if (strength >= TertiaryLevel)
            {
                index = FindOrInsertWeakerNode(index, CollationWeights.Tertiary(ce), TertiaryLevel);
            }
        }

        return index;
    }

    private int FindOrInsertNodeForPrimary(uint p)
    {
        var rootIndex = _rootPrimaries.BinarySearch(p);
        if (rootIndex >= 0)
            return _rootPrimaryIndexes[rootIndex];

        // Start a new list of nodes with this primary
        var index = _nodes.Count;
        _nodes.Add(new Node { Weight = p, Strength = PrimaryLevel });
        _rootPrimaries.Insert(~rootIndex, p);
        _rootPrimaryIndexes.Insert(~rootIndex, index);
        return index;
    }

    private int FindOrInsertWeakerNode(int index, uint weight16, int level)
    {
        if (weight16 == CollationWeights.CommonWeight16)
            return FindCommonNode(index, level);

        // If this will be the first below-common weight for the parent node, then we will also need to insert a common weight after it
        var node = _nodes[index];
        if (weight16 != 0 && weight16 < CollationWeights.CommonWeight16)
        {
            var hasThisLevelBefore = level == SecondaryLevel ? node.HasBefore2 : node.HasBefore3;
            if (!hasThisLevelBefore)
            {
                // The parent node has an implied level-common weight
                var commonNode = new Node { Weight = CollationWeights.CommonWeight16, Strength = level };
                if (level == SecondaryLevel)
                {
                    // Move the HAS_BEFORE3 flag from the parent node to the new secondary common node
                    commonNode.HasBefore3 = node.HasBefore3;
                    node.HasBefore3 = false;
                    node.HasBefore2 = true;
                }
                else
                {
                    node.HasBefore3 = true;
                }

                _nodes[index] = node;
                var nextIndex2 = node.Next;
                var belowIndex = InsertNodeBetween(index, nextIndex2, new Node { Weight = weight16, Strength = level });
                InsertNodeBetween(belowIndex, nextIndex2, commonNode);
                return belowIndex;
            }
        }

        // Find the root CE's weight for this level; insert the new root node before the next stronger node, or before
        // the next root node with the same strength and a larger weight
        int nextIndex;
        while ((nextIndex = node.Next) != 0)
        {
            node = _nodes[nextIndex];
            var nextStrength = node.Strength;
            if (nextStrength <= level)
            {
                // Insert before a stronger node
                if (nextStrength < level)
                    break;

                if (!node.IsTailored)
                {
                    var nextWeight16 = node.Weight;
                    if (nextWeight16 == weight16)
                        return nextIndex;

                    // Insert before a node with a larger same-strength weight
                    if (nextWeight16 > weight16)
                        break;
                }
            }

            // Skip the next node
            index = nextIndex;
        }

        return InsertNodeBetween(index, nextIndex, new Node { Weight = weight16, Strength = level });
    }

    private int InsertTailoredNodeAfter(int index, int strength)
    {
        if (strength >= SecondaryLevel)
        {
            index = FindCommonNode(index, SecondaryLevel);
            if (strength >= TertiaryLevel)
            {
                index = FindCommonNode(index, TertiaryLevel);
            }
        }

        // Postpone insertion: insert the new node before the next one with a strength at least as strong
        var node = _nodes[index];
        int nextIndex;
        while ((nextIndex = node.Next) != 0)
        {
            node = _nodes[nextIndex];
            if (node.Strength <= strength)
                break;

            // Skip the next node which has a weaker (larger) strength than the new one
            index = nextIndex;
        }

        return InsertNodeBetween(index, nextIndex, new Node { IsTailored = true, Strength = strength });
    }

    private int InsertNodeBetween(int index, int nextIndex, Node node)
    {
        var newIndex = _nodes.Count;
        node.Previous = index;
        node.Next = nextIndex;
        _nodes.Add(node);

        var span = CollectionsMarshal.AsSpan(_nodes);
        span[index].Next = newIndex;
        if (nextIndex != 0)
        {
            span[nextIndex].Previous = newIndex;
        }

        return newIndex;
    }

    private int FindCommonNode(int index, int strength)
    {
        var node = _nodes[index];
        if (node.Strength >= strength)
            return index; // The current node is no stronger

        if (strength == SecondaryLevel ? !node.HasBefore2 : !node.HasBefore3)
            return index; // The current node implies the strength-common weight

        index = node.Next;
        node = _nodes[index];

        // Skip to the explicit common node
        do
        {
            index = node.Next;
            node = _nodes[index];
        }
        while (node.IsTailored || node.Strength > strength || node.Weight < CollationWeights.CommonWeight16);

        return index;
    }

    private static int CountTailoredNodes(List<Node> nodes, int i, int strength)
    {
        var count = 0;
        while (i != 0)
        {
            var node = nodes[i];
            if (node.Strength < strength)
                break;

            if (node.Strength == strength)
            {
                if (node.IsTailored)
                {
                    count++;
                }
                else
                {
                    break;
                }
            }

            i = node.Next;
        }

        return count;
    }

    private ulong[] MakeTailoredCEs()
    {
        var result = new ulong[_nodes.Count];
        var primaries = new WeightAllocator();
        var secondaries = new WeightAllocator();
        var tertiaries = new WeightAllocator();
        for (var rpi = 0; rpi < _rootPrimaryIndexes.Count; rpi++)
        {
            var i = _rootPrimaryIndexes[rpi];
            var node = _nodes[i];
            var p = node.Weight;
            var s = p == 0 ? 0 : CollationWeights.CommonWeight16;
            var t = s;
            var pIsTailored = false;
            var sIsTailored = false;
            var tIsTailored = false;
            var rootP = p;
            var rootS = s;
            result[i] = CollationWeights.MakeCE(p, s, t);
            var nextIndex = node.Next;
            while (nextIndex != 0)
            {
                i = nextIndex;
                node = _nodes[i];
                nextIndex = node.Next;
                var strength = node.Strength;
                if (strength == QuaternaryLevel)
                {
                    // The quaternary level is not compared: a quaternary difference has the weights of its parent
                }
                else if (strength == TertiaryLevel)
                {
                    if (node.IsTailored)
                    {
                        if (!tIsTailored)
                        {
                            // First tailored tertiary node for [p, s]
                            var tCount = CountTailoredNodes(_nodes, nextIndex, TertiaryLevel) + 1;
                            uint tLimit;
                            if (t == 0)
                            {
                                // Gap at the beginning of the tertiary CE range: above the other tertiary weights, as ICU
                                t = CollationWeights.TertiaryBoundary - 0x100;
                                tLimit = CollationWeights.TertiaryBoundary;
                            }
                            else if (!pIsTailored && !sIsTailored)
                            {
                                // p and s are root weights
                                tLimit = _root.GetTertiaryAfter(rootP, rootS, t);
                            }
                            else if (t == CollationWeights.BeforeWeight16)
                            {
                                tLimit = CollationWeights.CommonWeight16;
                            }
                            else
                            {
                                tLimit = CollationWeights.TertiaryBoundary;
                            }

                            if (!tertiaries.Allocate(t, tLimit, tCount))
                                throw new FormatException("The tertiary tailoring gap is too small");

                            tIsTailored = true;
                        }

                        t = tertiaries.Next();
                    }
                    else
                    {
                        t = node.Weight;
                        tIsTailored = false;
                    }
                }
                else
                {
                    if (strength == SecondaryLevel)
                    {
                        if (node.IsTailored)
                        {
                            if (!sIsTailored)
                            {
                                // First tailored secondary node for p
                                var sCount = CountTailoredNodes(_nodes, nextIndex, SecondaryLevel) + 1;
                                uint sLimit;
                                if (s == 0)
                                {
                                    // Gap at the beginning of the secondary CE range: just below the secondaries of the combining marks, as ICU
                                    sLimit = (uint)(_root.FirstSecondaryCE >> 16) & 0xFFFF;
                                    s = sLimit - 0x100;
                                }
                                else if (!pIsTailored)
                                {
                                    // p is a root primary
                                    sLimit = _root.GetSecondaryAfter(rootP, s);
                                }
                                else if (s == CollationWeights.BeforeWeight16)
                                {
                                    sLimit = CollationWeights.CommonWeight16;
                                }
                                else
                                {
                                    sLimit = CollationWeights.SecondaryBoundary;
                                }

                                if (!secondaries.Allocate(s, sLimit, sCount))
                                    throw new FormatException("The secondary tailoring gap is too small");

                                sIsTailored = true;
                            }

                            s = secondaries.Next();
                        }
                        else
                        {
                            s = node.Weight;
                            rootS = s;
                            sIsTailored = false;
                        }
                    }
                    else
                    {
                        // Primary: always tailored after the root primary of the list
                        if (!pIsTailored)
                        {
                            var pCount = CountTailoredNodes(_nodes, nextIndex, PrimaryLevel) + 1;
                            var pLimit = _root.GetPrimaryAfter(p);
                            if (!primaries.Allocate(p, pLimit, pCount))
                                throw new FormatException("The primary tailoring gap is too small");

                            pIsTailored = true;
                        }

                        p = primaries.Next();
                        s = CollationWeights.CommonWeight16;
                        sIsTailored = false;
                    }

                    t = s == 0 ? 0 : CollationWeights.CommonWeight16;
                    tIsTailored = false;
                }

                result[i] = CollationWeights.MakeCE(p, s, t);
            }
        }

        return result;
    }

    /// <summary>Allocates consecutive weights at the start of a gap, as ICU (the smallest weights after the lower limit).</summary>
    private sealed class WeightAllocator
    {
        private uint _next;

        public bool Allocate(uint lowExclusive, uint highExclusive, int count)
        {
            _next = lowExclusive;
            return (ulong)lowExclusive + (ulong)count < highExclusive;
        }

        public uint Next() => ++_next;
    }

    // A UnicodeSet of [suppressContractions]: characters, ranges (a-z) and escapes (\uXXXX), between brackets
    private static List<int> ParseUnicodeSet(string set)
    {
        var codePoints = new List<int>();
        var text = set.Trim();
        if (text.StartsWith('[', StringComparison.Ordinal))
        {
            text = text[1..];
        }

        if (text.EndsWith(']', StringComparison.Ordinal))
        {
            text = text[..^1];
        }

        var items = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
                continue;

            if (c == '-' && items.Count > 0)
            {
                items.Add(-1);
                continue;
            }

            int codePoint;
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is 'u' or 'U')
            {
                var length = text[i + 1] == 'u' ? 4 : 8;
                codePoint = Convert.ToInt32(text.Substring(i + 2, length), 16);
                i += 1 + length;
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length)
            {
                codePoint = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            else
            {
                codePoint = c;
            }

            items.Add(codePoint);
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] < 0)
                continue;

            if (i + 2 < items.Count && items[i + 1] < 0)
            {
                for (var codePoint = items[i]; codePoint <= items[i + 2]; codePoint++)
                {
                    codePoints.Add(codePoint);
                }

                i += 2;
            }
            else
            {
                codePoints.Add(items[i]);
            }
        }

        return codePoints;
    }
}
