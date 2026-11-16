function move()
    print("Contrôle Lua", true, nil)
    local plan, reason = craft:plan(16520, 1)
    if not plan then
        printWarning("Plan indisponible", reason)
        return false
    end
    for _, step in ipairs(plan.crafts) do
        print(step.name, step.crafts, step.job, step.jobLevel)
    end
    for _, item in ipairs(plan.materials) do
        print(item.name, item.missing)
    end
    return false
end
