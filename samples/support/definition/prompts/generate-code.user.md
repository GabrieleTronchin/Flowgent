Issue ID: {{ run.id }}
Root Cause: {{ steps.resolve.rootCauseDescription }}
Affected Component: {{ steps.resolve.affectedComponent }}
Severity: {{ steps.resolve.severityAssessment }}
Proposed Fix: {{ steps.resolve.proposedFixSummary }}
File: {{ steps.locate_component.path }}
{{~ if steps.locate_component.found }}

Current content:
```{{ steps.locate_component.language }}
{{ steps.locate_component.snippet }}
```
{{~ end }}
