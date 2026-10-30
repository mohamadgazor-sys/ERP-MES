using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

// Item numbering and assemblies (Decision Log #121): RequestNo-001, children ParentNo.001, never reused, nested to four levels, locked once stages exist.
public class ItemHierarchyTests
{
    private static readonly DateTime Later = RequestFixtures.Now.AddHours(1);

    private static RequestItem Define(Request request, string? customerRef, RequestItem? parent = null, bool assembly = false) =>
        request.AddItem(0, customerRef, "Part " + customerRef, 1, 1, RequestFixtures.Now, Priority.Normal, parent, assembly);

    private static void CompleteRoute(RequestItem item)
    {
        item.DefineInitialStage("Turning", 5m);
        item.ReleaseFirstStage();
        item.StartStage(1);
        item.SubmitStageForQc(1);
        item.ApproveStageWholeQuantity(1, 1, Later);
    }

    [Fact]
    public void Items_without_a_parent_are_numbered_with_the_request_number_and_a_three_digit_sequence_and_the_request_number_keeps_its_case()
    {
        var request = RequestFixtures.NewRequest("wsj-898");

        var first = request.AddRing("A");
        var second = request.AddRing("B");

        Assert.Equal("wsj-898-001", first.ItemNo);
        Assert.Equal("wsj-898-002", second.ItemNo);
        Assert.Equal(1, first.ItemSequence);
        Assert.Null(first.Parent);
        Assert.False(first.IsAssembly);
    }

    [Fact]
    public void Children_follow_the_hierarchy_and_each_parent_has_its_own_sequence_starting_at_one()
    {
        var request = RequestFixtures.NewRequest("P-565");
        var parent = Define(request, "P", assembly: true);
        var other = Define(request, "Q", assembly: true);
        var firstChild = Define(request, "P1", parent, assembly: true);
        var secondChild = Define(request, "P2", parent);
        var otherChild = Define(request, "Q1", other);
        var grandChild = Define(request, "P11", firstChild, assembly: true);
        var greatGrandChild = Define(request, "P111", grandChild);

        Assert.Equal("P-565-001", parent.ItemNo);
        Assert.Equal("P-565-001.001", firstChild.ItemNo);
        Assert.Equal("P-565-001.002", secondChild.ItemNo);
        Assert.Equal("P-565-002.001", otherChild.ItemNo);
        Assert.Equal("P-565-001.001.001", grandChild.ItemNo);
        Assert.Equal("P-565-001.001.001.001", greatGrandChild.ItemNo);
        Assert.Equal(new[] { firstChild, secondChild }, request.ChildrenOf(parent));
        Assert.Equal(new[] { firstChild, secondChild, grandChild, greatGrandChild }.OrderBy(i => i.ItemNo, StringComparer.Ordinal), request.DescendantsOf(parent).OrderBy(i => i.ItemNo, StringComparer.Ordinal));
        Assert.Equal(4, Request.DepthOf(greatGrandChild));
    }

    [Fact]
    public void A_parent_must_be_an_assembly_and_nesting_stops_at_four_levels()
    {
        var request = RequestFixtures.NewRequest();
        var plain = Define(request, "plain");
        Assert.Equal(ItemHierarchyProblem.ParentNotAssembly, Assert.Throws<ItemHierarchyException>(() => Define(request, "child", plain)).Problem);

        var level1 = Define(request, "L1", assembly: true);
        var level2 = Define(request, "L2", level1, assembly: true);
        var level3 = Define(request, "L3", level2, assembly: true);
        var level4 = Define(request, "L4", level3, assembly: true);

        Assert.Equal(4, Request.DepthOf(level4));
        Assert.Equal(ItemHierarchyProblem.TooDeep, Assert.Throws<ItemHierarchyException>(() => Define(request, "L5", level4)).Problem);

        // Marking the plain item as an assembly first makes the child possible.
        request.MarkAssembly(plain);
        Assert.Equal("P-565-001.001", Define(request, "child", plain).ItemNo);
    }

    [Fact]
    public void A_parent_from_another_request_is_refused()
    {
        var request = RequestFixtures.NewRequest("A-1", requestId: 1);
        var other = RequestFixtures.NewRequest("B-2", requestId: 2);
        var foreign = Define(other, "X", assembly: true);

        Assert.Equal(ItemHierarchyProblem.OtherRequest, Assert.Throws<ItemHierarchyException>(() => Define(request, "Y", foreign)).Problem);
        Assert.Empty(request.Items);
    }

    [Fact]
    public void Numbers_are_never_reused_not_even_when_the_highest_item_moves_to_another_assembly()
    {
        var request = RequestFixtures.NewRequest();
        var first = Define(request, "A", assembly: true);
        var second = Define(request, "B", assembly: true);
        var child = Define(request, "A1", first);
        Assert.Equal("P-565-001.001", child.ItemNo);

        request.MoveItem(child, second, Later);
        var next = Define(request, "A2", first);

        Assert.Equal("P-565-002.001", child.ItemNo);
        Assert.Equal("P-565-001.002", next.ItemNo); // .001 is not given again
        Assert.Equal("A1", child.CustomerItemRef);
    }

    [Fact]
    public void The_nine_hundred_ninety_nine_limit_is_a_clear_error_and_the_counter_does_not_wrap()
    {
        var request = RequestFixtures.NewRequest();
        for (var i = 1; i <= Request.MaxItemSequence; i++)
        {
            Define(request, null);
        }

        Assert.Equal("P-565-999", request.Items[^1].ItemNo);
        Assert.Equal(ItemHierarchyProblem.SequenceExhausted, Assert.Throws<ItemHierarchyException>(() => Define(request, null)).Problem);
        Assert.Equal(Request.MaxItemSequence, request.Items.Count);
    }

    [Fact]
    public void A_move_renumbers_the_whole_subtree_validates_cycles_and_keeps_customer_references()
    {
        var request = RequestFixtures.NewRequest();
        var a = Define(request, "A", assembly: true);
        var b = Define(request, "B", assembly: true);
        var b1 = Define(request, "B1", b, assembly: true);
        var b11 = Define(request, "B11", b1);
        var a1 = Define(request, "A1", a);

        request.MoveItem(b, a, Later);

        Assert.Equal("P-565-001.002", b.ItemNo);
        Assert.Equal("P-565-001.002.001", b1.ItemNo);
        Assert.Equal("P-565-001.002.001.001", b11.ItemNo);
        Assert.Equal(new[] { "B", "B1", "B11" }, new[] { b, b1, b11 }.Select(i => i.CustomerItemRef).ToArray());
        Assert.Equal("P-565-001.001", a1.ItemNo);

        // An item cannot move below itself or below its own descendants; moving to the top level takes the next root sequence.
        Assert.Equal(ItemHierarchyProblem.Cycle, Assert.Throws<ItemHierarchyException>(() => request.MoveItem(b, b11, Later)).Problem);
        Assert.Equal(ItemHierarchyProblem.Cycle, Assert.Throws<ItemHierarchyException>(() => request.MoveItem(a, b1, Later)).Problem);
        request.MoveItem(b, null, Later);
        Assert.Equal("P-565-003", b.ItemNo);
        Assert.Equal("P-565-003.001.001", b11.ItemNo);
        Assert.Equal(ItemHierarchyProblem.ParentNotAssembly, Assert.Throws<ItemHierarchyException>(() => request.MoveItem(b, a1, Later)).Problem);
    }

    [Fact]
    public void A_move_that_would_nest_too_deep_is_refused()
    {
        var request = RequestFixtures.NewRequest();
        var l1 = Define(request, "L1", assembly: true);
        var l2 = Define(request, "L2", l1, assembly: true);
        var l3 = Define(request, "L3", l2, assembly: true);
        var top = Define(request, "T", assembly: true);
        var topChild = Define(request, "T1", top, assembly: true);

        // T has height 1; under L3 (depth 3) it would reach depth 5.
        Assert.Equal(ItemHierarchyProblem.TooDeep, Assert.Throws<ItemHierarchyException>(() => request.MoveItem(top, l3, Later)).Problem);
        Assert.Null(top.Parent);
        Assert.Equal("P-565-002.001", topChild.ItemNo);
    }

    [Fact]
    public void Once_a_stage_is_defined_in_the_subtree_or_in_either_parent_the_relationship_is_locked()
    {
        var request = RequestFixtures.NewRequest();
        var oldParent = Define(request, "OLD", assembly: true);
        var newParent = Define(request, "NEW", assembly: true);
        var child = Define(request, "C", oldParent, assembly: true);
        var grandChild = Define(request, "G", child);

        grandChild.DefineInitialStage("Turning", 1m);
        Assert.Equal(ItemHierarchyProblem.Locked, Assert.Throws<ItemHierarchyException>(() => request.MoveItem(child, newParent, Later)).Problem);

        var other = Define(request, "O", oldParent);
        newParent.DefineInitialStage("Assembly", 1m);
        Assert.Equal(ItemHierarchyProblem.Locked, Assert.Throws<ItemHierarchyException>(() => request.MoveItem(other, newParent, Later)).Problem);
        Assert.Equal(ItemHierarchyProblem.Locked, Assert.Throws<ItemHierarchyException>(() => Define(request, "late", newParent)).Problem);

        oldParent.DefineInitialStage("Assembly", 1m);
        Assert.Equal(ItemHierarchyProblem.Locked, Assert.Throws<ItemHierarchyException>(() => request.MoveItem(other, null, Later)).Problem);
        Assert.Same(oldParent, other.Parent);
    }

    [Fact]
    public void An_assembly_with_children_cannot_be_turned_back_into_a_normal_item()
    {
        var request = RequestFixtures.NewRequest();
        var assembly = Define(request, "A", assembly: true);
        var alone = Define(request, "B", assembly: true);
        Define(request, "A1", assembly);

        Assert.Equal(ItemHierarchyProblem.HasChildren, Assert.Throws<ItemHierarchyException>(() => request.UnmarkAssembly(assembly)).Problem);
        Assert.True(assembly.IsAssembly);

        request.UnmarkAssembly(alone);
        Assert.False(alone.IsAssembly);
    }

    [Fact]
    public void An_assembly_is_ready_for_release_only_when_every_descendant_has_a_completed_and_approved_route()
    {
        var request = RequestFixtures.NewRequest();
        var assembly = Define(request, "A", assembly: true);
        var child = Define(request, "A1", assembly, assembly: true);
        var grandChild = Define(request, "A11", child);

        Assert.True(request.AllDescendantsRouteCompleted(grandChild)); // an item without children is always ready
        Assert.False(request.AllDescendantsRouteCompleted(assembly)); // children with no stages are not complete

        CompleteRoute(grandChild);
        Assert.False(request.AllDescendantsRouteCompleted(assembly)); // the child itself is still open
        CompleteRoute(child);
        Assert.True(request.AllDescendantsRouteCompleted(assembly));
    }

    [Fact]
    public void Cancelled_and_closed_requests_accept_no_hierarchy_changes()
    {
        var request = RequestFixtures.NewRequest();
        var assembly = Define(request, "A", assembly: true);
        var plain = Define(request, "B");
        request.SetStatus(RequestStatus.Cancelled);

        Assert.Throws<InvalidOperationException>(() => Define(request, "late", assembly));
        Assert.Throws<InvalidOperationException>(() => request.MoveItem(plain, assembly, Later));
        Assert.Throws<InvalidOperationException>(() => request.MarkAssembly(plain));
    }
}
