-- One-shot smoke measurement; not a session strategy or profitability claim.
local economics = require("Measured-Session-Economics-20261101")
economics.start()
local ran = false

function move()
    if not ran then
        ran = true
        economics.finish()
    end
    return false
end

function stopped(reason)
    economics.finish()
end
