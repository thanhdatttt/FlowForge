namespace FlowForge.Api.Contracts.Projects;

public sealed record ProjectPageDto(IReadOnlyList<ProjectResponseDto> Items, int Total, int Page, int PageSize);
