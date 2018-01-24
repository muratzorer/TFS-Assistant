# TFS-Assistant
CLI tool for easily merging TFS changesets linked to a WorkItem. Shines when using Branch by Quality (Environment Branching)

## Usage Example

	`merge --collection "http://127.0.0.1:8080/Tfs/DefaultCollection" --workspace "MyPc001" --source "$/Project/Solution/Dev" --target "$/Project/Solution/Test" --workitem 1234`
	

	```
	-c, --collection    Required. Collection URL. eg: 'http://127.0.0.1:8080/Tfs/DefaultCollection'

	-s, --workspace     Required. Workspace name. eg: 'MyPc001'

	-w, --workitem      Required. Workitem ID. eg: '1234'

	-s, --source        Required. ServerPath (after collection name) for source branch. eg: '$/Project/Solution/Dev'

	-t, --target        Required. ServerPath (after collection name) for target branch. eg: '$/Project/Solution/Test'

	--help              Display this help screen.

	--version           Display version information.
	```