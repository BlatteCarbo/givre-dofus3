-- GivreClone : retirer exactement 1 000 000 kamas de la banque.
-- Demarrer le script sur la carte du banquier.

function move()
    local montant = 1000000

    if not bank:open() then
        printError("Impossible d'ouvrir la banque : place-toi devant un banquier.")
        return false
    end

    local solde = bank:getKamas()
    if solde < montant then
        printWarning("Kamas insuffisants en banque : " .. tostring(solde))
        bank:close()
        return false
    end

    local ok = bank:withdrawKamas(montant)
    bank:close()

    if ok then
        printSuccess("1 000 000 kamas retires de la banque.")
    else
        printError("Le retrait n'a pas ete confirme par le serveur.")
    end

    return false -- terminer le script apres une tentative
end
