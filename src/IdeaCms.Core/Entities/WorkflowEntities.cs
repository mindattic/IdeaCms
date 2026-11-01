namespace IdeaCms.Core.Entities;

/// <summary>
/// A named content workflow: defines a set of state names and the role-gated transitions between them.
/// The workflow is global (host-level). Pages opt in via <see cref="Page.WorkflowDefinitionId"/>; the site
/// can designate one definition as default (<see cref="IsDefault"/>). Widget manifests may declare a
/// suggested workflow name via <c>defaultWorkflow</c>.
/// </summary>
public sealed class WorkflowDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";              // unique display name, e.g. "Default", "Editorial"
    public string Description { get; set; } = "";
    public string InitialState { get; set; } = "Draft"; // state assigned when a page first adopts this workflow
    public bool IsDefault { get; set; }                 // at most one; enforced at service layer
    public DateTime CreatedUtc { get; set; }

    public ICollection<WorkflowTransitionDef> Transitions { get; set; } = [];
}

/// <summary>
/// A role-gated state transition within a <see cref="WorkflowDefinition"/>. Authorizes a move from
/// <see cref="FromState"/> to <see cref="ToState"/> for users that hold <see cref="RequiredRole"/>
/// (or any authenticated user when <see cref="RequiredRole"/> is null).
/// </summary>
public sealed class WorkflowTransitionDef
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public string FromState { get; set; } = "";         // origin state name, e.g. "Draft"
    public string ToState { get; set; } = "";           // destination state name, e.g. "Published"
    public string? RequiredRole { get; set; }           // null = any authenticated user; "Admin" = admins only
    public string? Label { get; set; }                  // human-readable action name, e.g. "Publish"
}
