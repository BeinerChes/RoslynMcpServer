# Benchmark Task: Add Save Validation

## The Request

"Add validation to `FeatureSet.Save()` that prevents saving empty feature sets. Throw `InvalidOperationException` if `NumShapes == 0`."

## Target

- **Solution:** `D:\repos\Atlas3_EDEV\Atlas3.sln`
- **Class:** `Atlas.Data.FeatureSet`

## Steps

1. **Understand the class** - How many members? Where are they defined?
2. **Read Save() method** - Get the full implementation
3. **Find all callers** - Who calls Save()? They may need to handle the new exception
4. **Analyze impact** - What breaks if Save() throws? Trace transitive callers
5. **Add validation method** - Create `ValidateBeforeSave()` that throws if empty
6. **Update Save()** - Call validation at the start
7. **Find validation patterns** - Are there other Validate* methods to follow?
8. **Rename if needed** - Match existing naming conventions
9. **Check compilation** - Any errors after our changes?
10. **Find dead code** - Did we create unused code?
11. **Clean up** - Delete the validation method (benchmark only)

## Success Criteria

- All 11 steps completed
- Changes compile without errors
- All modifications reverted at the end
