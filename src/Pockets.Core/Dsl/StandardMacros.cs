namespace Pockets.Core.Dsl;

/// <summary>
/// Standard DSL macro definitions for context-sensitive actions.
/// These define primary and secondary actions as cond dispatch tables
/// over query opcodes, matching the behavior of GameState.ToolPrimary/ToolSecondary.
/// </summary>
public static class StandardMacros
{
    /// <summary>
    /// Primary action (left-click / key 1). Decision tree:
    /// 1. Output slot + not empty + hand empty → grab
    /// 2. Cell has bag → enter
    /// 3. Hand empty + cell empty → no-op (sort as identity)
    /// 4. Hand empty + nested → harvest
    /// 5. Hand empty + cell occupied → grab
    /// 6. Hand full + cell empty → drop
    /// 7. Hand full + same type → drop (merge)
    /// 8. Hand full + different type → swap
    /// </summary>
    public const string PrimaryDsl = @"
        [
            [ output-slot? cell-empty? not and hand-empty? and ] [ grab ]
            [ cell-has-bag? ]                                    [ enter ]
            [ hand-empty? cell-empty? and ]                      [ ]
            [ hand-empty? nested? and ]                          [ harvest ]
            [ hand-empty? ]                                      [ grab ]
            [ cell-empty? ]                                      [ drop ]
            [ same-type? ]                                       [ drop ]
            [ true ]                                             [ swap ]
        ] cond
    ";

    /// <summary>
    /// Secondary action (right-click / key 2). Decision tree:
    /// 1. Hand empty + (cell empty or count ≤ 1) → no-op
    /// 2. Hand empty → grab-half
    /// 3. Hand full + (cell empty or same type) → drop-one
    /// 4. Otherwise → no-op
    /// </summary>
    public const string SecondaryDsl = @"
        [
            [ hand-empty? cell-empty? and ]                      [ ]
            [ hand-empty? cell-count 1 lte and ]                 [ ]
            [ hand-empty? ]                                      [ grab-half ]
            [ cell-empty? same-type? or ]                        [ drop-one ]
            [ true ]                                             [ ]
        ] cond
    ";
}
