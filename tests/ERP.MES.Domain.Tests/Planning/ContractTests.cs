using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Planning;

public class ContractTests
{
    private static readonly DateTime At = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly WorkflowActor Planning = new(1, "PLANNING");
    private static readonly WorkflowActor Management = new(2, "MANAGEMENT");
    private static readonly ContractEligibility Ok = new(WorkType.Make, RequestStatus.DrawingReturnedToPlanning, DrawingOutcome.FullyAccepted);

    private static StoredDocument Pdf(string text = "x")
    {
        var content = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 " + text);
        Assert.True(StoredDocument.TryCreateContractPdf("c.pdf", content, new string('A', 64), 1_000_000, 1, At, out var document, out _));
        return document!;
    }

    private static RequestContract Required() => RequestContract.Decide(10, Ok, ContractRequirement.Required, null, Planning, At);

    [Theory]
    [InlineData(WorkType.Repair, RequestStatus.DrawingReturnedToPlanning, DrawingOutcome.FullyAccepted, ContractIneligibility.NotMake)]
    [InlineData(WorkType.Make, RequestStatus.MgmtApprovedAtPlanning, DrawingOutcome.FullyAccepted, ContractIneligibility.RequestNotReturnedFromDrawing)]
    [InlineData(WorkType.Make, RequestStatus.UnderDrawingReview, null, ContractIneligibility.RequestNotReturnedFromDrawing)]
    [InlineData(WorkType.Make, RequestStatus.DrawingReturnedToPlanning, DrawingOutcome.Rejected, ContractIneligibility.DrawingResultNotAccepted)]
    [InlineData(WorkType.Make, RequestStatus.DrawingReturnedToPlanning, null, ContractIneligibility.DrawingResultNotAccepted)]
    [InlineData(WorkType.Make, RequestStatus.DrawingReturnedToPlanning, DrawingOutcome.PartiallyAccepted, ContractIneligibility.None)]
    public void Eligibility_follows_type_status_and_drawing_result(WorkType type, RequestStatus status, DrawingOutcome? outcome, ContractIneligibility expected) =>
        Assert.Equal(expected, new ContractEligibility(type, status, outcome).Check());

    [Fact]
    public void An_ineligible_request_cannot_be_decided()
    {
        var repair = new ContractEligibility(WorkType.Repair, RequestStatus.DrawingReturnedToPlanning, DrawingOutcome.FullyAccepted);

        var thrown = Assert.Throws<ContractGateException>(() => RequestContract.Decide(1, repair, ContractRequirement.NotRequired, null, Planning, At));
        Assert.Equal(ContractIneligibility.NotMake, thrown.Reason);
    }

    [Fact]
    public void Readiness_requires_the_contract_only_for_make_and_only_when_it_is_approved()
    {
        var returned = RequestStatus.DrawingReturnedToPlanning;
        var accepted = DrawingOutcome.FullyAccepted;

        Assert.Equal(DefinitionBlock.None, DefinitionReadiness.Evaluate(WorkType.Repair, returned, accepted, null));
        Assert.Equal(DefinitionBlock.ContractDecisionMissing, DefinitionReadiness.Evaluate(WorkType.Make, returned, accepted, null));
        Assert.Equal(DefinitionBlock.None, DefinitionReadiness.Evaluate(WorkType.Make, returned, accepted, ContractStatus.NotRequired));
        Assert.Equal(DefinitionBlock.None, DefinitionReadiness.Evaluate(WorkType.Make, returned, accepted, ContractStatus.Approved));
        Assert.Equal(DefinitionBlock.ContractInPreparation, DefinitionReadiness.Evaluate(WorkType.Make, returned, accepted, ContractStatus.Draft));
        Assert.Equal(DefinitionBlock.ContractPendingManagement, DefinitionReadiness.Evaluate(WorkType.Make, returned, accepted, ContractStatus.PendingManagement));
        Assert.Equal(DefinitionBlock.ContractRejected, DefinitionReadiness.Evaluate(WorkType.Make, returned, accepted, ContractStatus.RejectedAtPlanning));
        Assert.Equal(DefinitionBlock.RequestNotReturnedFromDrawing, DefinitionReadiness.Evaluate(WorkType.Make, RequestStatus.SentToManagement, accepted, ContractStatus.Approved));
        Assert.Equal(DefinitionBlock.DrawingResultNotAccepted, DefinitionReadiness.Evaluate(WorkType.Repair, returned, DrawingOutcome.Rejected, null));
    }

    [Fact]
    public void A_submitted_version_is_immutable()
    {
        var contract = Required();
        contract.SaveDraft(Ok, "CN-1", new DateOnly(2026, 10, 2), null, null, Planning, At);
        contract.AttachPdf(Ok, Pdf(), Planning, At);
        contract.Submit(Ok, Planning, At);

        Assert.Throws<InvalidOperationException>(() => contract.SaveDraft(Ok, "CN-2", null, null, null, Planning, At));
        Assert.Throws<InvalidOperationException>(() => contract.AttachPdf(Ok, Pdf("y"), Planning, At));
        Assert.Throws<InvalidOperationException>(() => contract.Submit(Ok, Planning, At));
        Assert.Equal("CN-1", contract.Versions.Single().ContractNo);
    }

    [Fact]
    public void Submission_names_what_is_missing()
    {
        var contract = Required();

        Assert.Equal(ContractSubmissionProblem.ContractNoMissing, Assert.Throws<ContractSubmissionException>(() => contract.Submit(Ok, Planning, At)).Problem);
        contract.SaveDraft(Ok, "CN-1", null, null, null, Planning, At);
        Assert.Equal(ContractSubmissionProblem.ContractDateMissing, Assert.Throws<ContractSubmissionException>(() => contract.Submit(Ok, Planning, At)).Problem);
        contract.SaveDraft(Ok, "CN-1", new DateOnly(2026, 10, 2), null, null, Planning, At);
        Assert.Equal(ContractSubmissionProblem.PdfMissing, Assert.Throws<ContractSubmissionException>(() => contract.Submit(Ok, Planning, At)).Problem);
    }

    [Fact]
    public void Rejection_needs_a_reason_and_approval_does_not()
    {
        var contract = Required();
        contract.SaveDraft(Ok, "CN-1", new DateOnly(2026, 10, 2), null, null, Planning, At);
        contract.AttachPdf(Ok, Pdf(), Planning, At);
        contract.Submit(Ok, Planning, At);

        Assert.Throws<ArgumentException>(() => contract.Reject(Ok, " ", Management, At));
        Assert.Equal(ContractStatus.PendingManagement, contract.Status);
        contract.Approve(Ok, null, Management, At);

        Assert.True(contract.IsSatisfied);
        Assert.Throws<InvalidOperationException>(() => contract.Reject(Ok, "late", Management, At));
        Assert.Throws<InvalidOperationException>(() => contract.Approve(Ok, null, Management, At));
    }

    [Fact]
    public void Every_action_is_refused_when_the_request_is_no_longer_eligible()
    {
        var contract = Required();
        var moved = new ContractEligibility(WorkType.Make, RequestStatus.UnderDrawingReview, DrawingOutcome.FullyAccepted);

        Assert.Throws<ContractGateException>(() => contract.SaveDraft(moved, "CN", null, null, null, Planning, At));
        Assert.Throws<ContractGateException>(() => contract.Submit(moved, Planning, At));
        Assert.Throws<ContractGateException>(() => contract.Approve(moved, null, Management, At));
        Assert.Throws<ContractGateException>(() => contract.Reject(moved, "x", Management, At));
    }
}
