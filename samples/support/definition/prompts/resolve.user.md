Agent: {{ run.agentId }} ({{ steps.assign_agent.role }})
Subject: {{ input.subject }}

Body: {{ input.body }}
{{~ if steps.locate_component.found }}

Source file {{ steps.locate_component.path }}:
```{{ steps.locate_component.language }}
{{ steps.locate_component.snippet }}
```
{{~ end }}
