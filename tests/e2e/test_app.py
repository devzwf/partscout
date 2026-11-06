#!/usr/bin/env python3
"""Parcours complet dans Chromium, contre une instance de démo NEUVE (le test crée le mot de passe).

Usage : python tests/e2e/test_app.py http://127.0.0.1:5079 [dossier-captures]
"""
import asyncio
import pathlib
import re
import sys

from playwright.async_api import async_playwright, expect

BASE = sys.argv[1].rstrip("/")
SHOTS = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else None
PASSWORD = "mot-de-passe-e2e-solide"
passed = []


def ok(name):
    passed.append(name)
    print("ok  ", name)


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        ctx = await browser.new_context(viewport={"width": 1440, "height": 900}, accept_downloads=True, locale="fr-CA")
        page = await ctx.new_page()
        problems = []
        page.on("pageerror", lambda e: problems.append(f"exception: {e}"))
        # 401 attendu : l'application vérifie la session. Le reste serait un vrai problème.
        page.on("console", lambda m: problems.append(f"console: {m.text}") if m.type == "error" and "401" not in m.text else None)

        # ---- Compte ----
        await page.goto(BASE)
        await page.fill('input[autocomplete="new-password"] >> nth=0', "court")
        await page.fill('input[autocomplete="new-password"] >> nth=1', "autre")
        await page.click("text=Créer le compte")
        await expect(page.locator("[role=alert]")).to_contain_text("diffèrent")
        await page.fill('input[autocomplete="new-password"] >> nth=0', "court")
        await page.fill('input[autocomplete="new-password"] >> nth=1', "court")
        await page.click("text=Créer le compte")
        await expect(page.locator("[role=alert]")).to_contain_text("10 caractères")
        await page.fill('input[autocomplete="new-password"] >> nth=0', PASSWORD)
        await page.fill('input[autocomplete="new-password"] >> nth=1', PASSWORD)
        await page.click("text=Créer le compte")
        await expect(page.locator(".welcome h1")).to_be_visible()
        await expect(page.locator(".notice")).to_contain_text("Mode démo")
        version = (pathlib.Path(__file__).resolve().parents[2] / "VERSION").read_text().strip()
        await expect(page.locator("header.top .version")).to_have_text("v" + version)
        ok("création du compte, avec erreurs de saisie")

        # ---- Recherche en flux ----
        await page.fill('input[aria-label="Recherche"]', "LM358")
        await page.keyboard.press("Enter")
        await expect(page.locator(".chip.wait").first).to_be_visible()  # des fournisseurs attendent encore
        await expect(page.locator("tbody.part").first).to_be_visible()  # alors que des résultats sont déjà là
        waiting_when_first_rows = await page.locator(".chip.wait").count()
        assert waiting_when_first_rows > 0, "les résultats doivent arriver avant la fin de tous les fournisseurs"
        await expect(page.locator(".chip.wait")).to_have_count(0, timeout=15000)
        await expect(page.locator(".foot")).to_contain_text("Terminé")
        assert "q=LM358" in page.url
        ok("résultats en direct, fournisseur par fournisseur")

        # Un même MPN chez 4 fournisseurs : une seule ligne avec 4 offres.
        first = page.locator("tbody.part", has_text="LM358DR").first
        await expect(first.locator(".col-offers")).to_have_text("4")
        await expect(page.locator(".chip.link", has_text="LCSC")).to_have_attribute("href", "https://www.lcsc.com/search?q=LM358")
        ok("regroupement par pièce, liens vers les sites")
        if SHOTS:
            await first.locator("tr.head").click()
            await page.screenshot(path=SHOTS / "resultats-sombre.png")
            await first.locator("tr.head").click()

        # ---- Meilleur prix : données de démo, taux démo CAD 1,52 / USD 1,08 ----
        # À 1 pièce : Mouser 0,59 CAD ; DigiKey 0,62 CAD ; TME MOQ 10 ; Newark MOQ 5.
        await expect(first.locator(".best")).to_contain_text("0,59")
        await expect(first.locator(".best")).to_contain_text("CAD")
        await first.locator("tr.head").click()
        await expect(first.locator("tr.top")).to_contain_text("Mouser")
        assert await first.locator("tr.top").evaluate("e => getComputedStyle(e).display") == "table-row"
        assert await page.locator("header.top").evaluate("e => e.offsetHeight") < 70, "en-tête sur deux lignes à 1440 px"
        await page.fill('.field input[type=number]', "100")
        # À 100 : TME 0,17 USD → 0,239 CAD, le moins cher.
        await expect(first.locator(".best")).to_contain_text("0,239")
        await expect(first.locator("tr.top")).to_contain_text("TME")
        assert "qty=100" in page.url
        ok("meilleur prix selon la quantité, MOQ et conversion compris")

        # ---- BOM : changer la quantité puis cliquer aussitôt ne doit pas perdre le clic ----
        await page.fill('.field input[type=number]', "20")
        await first.locator(".offer-table tbody tr", has_text="Mouser").locator("button.add").click()
        await expect(page.locator(".drawer.open")).to_be_visible()
        await expect(page.locator(".count")).to_have_text("1")
        await expect(page.locator('.line input[aria-label="Quantité"]')).to_have_value("20")
        # 20 pièces au palier de 10 : 0,43 → 8,60 CAD.
        await expect(page.locator(".totals .all")).to_contain_text("8,60 CAD")
        await first.locator(".offer-table tbody tr", has_text="Mouser").locator("button.add").click()
        await expect(page.locator('.line input[aria-label="Quantité"]')).to_have_value("40")
        await expect(page.locator(".count")).to_have_text("1")
        await first.locator(".offer-table tbody tr", has_text="TME").locator("button.add").click()
        await expect(page.locator(".count")).to_have_text("2")
        await page.fill('input[aria-label="Nom du BOM"]', "Alim labo")
        await page.locator('.line input[aria-label="Compter cette ligne"]').first.uncheck()
        await expect(page.locator(".line.dnp")).to_have_count(1)
        await page.locator('.line input[aria-label="Compter cette ligne"]').first.check()
        await page.locator('button[aria-label="Retirer la ligne"]').last.click()
        await expect(page.locator(".count")).to_have_text("1")
        await page.click("text=Annuler le retrait")
        await expect(page.locator(".count")).to_have_text("2")
        overflow = await page.evaluate("document.querySelector('.results').scrollWidth - document.querySelector('.results').clientWidth")
        assert overflow <= 1, f"débordement avec le BOM ouvert : {overflow}px"
        if SHOTS:
            await page.screenshot(path=SHOTS / "bom.png")
        await page.wait_for_timeout(800)  # enregistrement automatique
        await page.reload()
        await expect(page.locator(".count")).to_have_text("2")
        await expect(page.locator(".chip.wait")).to_have_count(0, timeout=15000)
        await page.click('button:has-text("BOM")')
        await expect(page.locator('input[aria-label="Nom du BOM"]')).to_have_value("Alim labo")
        ok("BOM : ajout, cumul, DNP, annulation, enregistrement après rechargement")

        # L'adresse a redonné la recherche et la quantité.
        await expect(page.locator('.field input[type=number]')).to_have_value("20")
        await expect(page.locator(".chip", has_text="DigiKey")).to_contain_text("cache")
        ok("URL partageable, résultats servis par le cache")

        # Export CSV, puis réimport.
        async with page.expect_download() as d:
            await page.click('.totals button:has-text("CSV")')
        path = await (await d.value).path()
        csv = pathlib.Path(path).read_bytes()
        assert csv.startswith(b"\xef\xbb\xbf\"Refs\",\"Value\",\"Footprint\""), csv[:40]
        bom_options = page.locator('select[aria-label="BOM ouvert"] option')
        boms = await bom_options.count()
        await page.set_input_files('.totals input[type=file]', {"name": "copie.csv", "mimeType": "text/csv", "buffer": csv})
        # L'import crée un BOM ; la liste se rafraîchit après le message, d'où l'attente avant de compter.
        await expect(bom_options).to_have_count(boms + 1)
        await expect(page.locator(".totals")).to_contain_text("2 ligne(s) importée(s)")
        await expect(page.locator('input[aria-label="Nom du BOM"]')).to_have_value("Alim labo")
        ok("BOM : export CSV et réimport")
        page.on("dialog", lambda dialog: asyncio.ensure_future(dialog.accept()))
        await page.click('.totals button:has-text("Supprimer")')
        await expect(bom_options).to_have_count(boms)
        await page.click('button[aria-label="Fermer le BOM"]')
        ok("BOM : suppression avec confirmation")

        # ---- Filtres ----
        all_parts = await page.locator("tbody.part").count()
        await page.locator(".facets label", has_text="Obsolète").locator("input").check()
        await expect(page.locator("tbody.part")).to_have_count(1)
        await expect(page.locator("tbody.part .mpn")).to_have_text("LM358AN/NOPB")
        await page.locator(".facets label", has_text="Obsolète").locator("input").uncheck()
        await expect(page.locator("tbody.part")).to_have_count(all_parts)
        await page.locator(".toggle input").check()
        await expect(page.locator("tbody.part", has_text="LM358AN/NOPB")).to_have_count(0)
        await page.locator(".toggle input").uncheck()
        await page.fill(".filter", "pwr")
        await expect(page.locator("tbody.part")).to_have_count(1)
        await page.fill(".filter", "aucun-resultat-possible")
        await expect(page.locator(".empty")).to_contain_text("masquées")
        await page.click("text=Tout réafficher")
        await expect(page.locator("tbody.part")).to_have_count(all_parts)
        await page.click('.chip:has-text("DigiKey")')
        await expect(page.locator(".chip.off")).to_have_count(1)
        await page.click('.chip:has-text("DigiKey")')
        ok("filtres : cycle de vie, stock, texte, fournisseur")

        # ---- Affichage par offre, tri, lignes masquées ----
        await page.click('button:has-text("Par offre")')
        rows = page.locator(".flat tbody tr")
        total = await rows.count()
        await page.click('.th:has-text("Prix")')
        prices = await page.locator(".flat tbody tr td:nth-child(10)").all_inner_texts()
        values = [float(x.split()[0].replace(",", ".").replace(" ", "").replace("\xa0", "")) for x in prices]
        assert values == sorted(values), values
        await rows.first.locator('button[title="Masquer cette ligne"]').click()
        await expect(rows).to_have_count(total - 1)
        await page.click("text=Rétablir 1 ligne")
        await expect(rows).to_have_count(total)
        await page.click('.th:has-text("Fournisseur")')
        await page.click('.th:has-text("N° fabricant")', modifiers=["Shift"])
        await expect(page.locator('.th:has-text("N° fabricant")')).to_contain_text("2")
        # Les actions restent à l'écran, quelle que soit la largeur, et un prix tient sur une ligne.
        for width in (1920, 1440, 1280, 1100):
            await page.set_viewport_size({"width": width, "height": 900})
            box = await page.locator(".flat tbody tr .mini.add").first.bounding_box()
            assert box and box["x"] + box["width"] <= width, f"« + BOM » hors de l'écran à {width} px : {box}"
            high = await page.evaluate("""Math.max(...[...document.querySelectorAll('.flat .price')].map(e => {
                const r = document.createRange(); r.selectNodeContents(e); return r.getBoundingClientRect().height }))""")
            assert high < 26, f"prix sur plusieurs lignes à {width} px : {high}"
        await page.set_viewport_size({"width": 1440, "height": 900})
        overflow = await page.evaluate("document.querySelector('.results').scrollWidth - document.querySelector('.results').clientWidth")
        assert overflow <= 1, f"la vue par offre déborde à 1440 px : {overflow} px"
        ok("vue par offre : tri simple et multiple, masquer et rétablir")

        # ---- Colonnes déplaçables : glisser, clavier, gardées au rechargement, ordre par défaut ----
        heads = "[...document.querySelectorAll('.flat>thead th')].map(e => e.textContent.replace(/[▴▾\\d]/g, '').trim())"
        reset = page.locator('button:has-text("Colonnes par défaut")')
        await expect(reset).to_have_count(0)
        before = await page.evaluate(heads)
        suppliers = await page.locator(f".flat tbody tr td:nth-child({before.index('Fournisseur') + 1})").all_inner_texts()
        th = page.locator(".flat>thead th")
        await th.nth(before.index("Fournisseur")).drag_to(th.nth(before.index("N° fabricant")))
        after = await page.evaluate(heads)
        assert after.index("Fournisseur") == before.index("N° fabricant"), (before, after)
        assert after.index("N° fabricant") < after.index("Fournisseur"), after
        # Les cellules suivent leur en-tête.
        cells = await page.locator(f".flat tbody tr td:nth-child({after.index('Fournisseur') + 1})").all_inner_texts()
        assert cells == suppliers, (suppliers[:5], cells[:5])
        await expect(reset).to_have_count(1)
        await th.nth(after.index("Stock")).locator(".th").focus()
        await page.keyboard.press("Alt+Shift+ArrowLeft")
        moved = await page.evaluate(heads)
        assert moved.index("Stock") == after.index("Stock") - 1, (after, moved)
        await page.reload()
        await expect(page.locator(".flat tbody tr").first).to_be_visible()
        assert await page.evaluate(heads) == moved, "ordre des colonnes perdu au rechargement"
        await page.click('button:has-text("Par pièce")')
        await expect(reset).to_have_count(0)
        await page.click('button:has-text("Par offre")')
        await reset.click()
        assert await page.evaluate(heads) == before
        await expect(reset).to_have_count(0)
        await page.click('button:has-text("Par pièce")')
        ok("colonnes déplaçables : glisser, clavier, rechargement, ordre par défaut")

        # ---- Charger plus ----
        await page.fill('input[aria-label="Recherche"]', "LM358X")
        await page.click("text=Avancée")
        # Une case ne doit pas prendre toute la largeur, loin de son libellé.
        widths = await page.evaluate("[...document.querySelectorAll('.modal .checks input[type=checkbox]')].map(e => e.getBoundingClientRect().width)")
        assert widths and max(widths) < 30, f"cases trop larges : {widths}"
        await page.fill('.form input[type=number]', "5")
        await page.click('.form button:has-text("Rechercher")')
        await expect(page.locator(".chip.wait")).to_have_count(0, timeout=15000)
        await page.click('button:has-text("Charger plus")')
        await expect(page.locator("tbody.part", has_text="LM358DRG4")).to_have_count(1)
        # Le BOM fermé est hors champ à droite : la zone centrale ne doit jamais défiler vers lui.
        shift = await page.evaluate("document.querySelector('.content').scrollLeft")
        assert shift == 0, f"zone centrale décalée de {shift} px"
        ok("charger plus")

        # ---- Recherche avancée : filtres côté application ----
        await page.click("text=Avancée")
        await page.get_by_label("Fabricant contenant", exact=True).fill("stmicro")
        await page.click('.form button:has-text("Rechercher")')
        await expect(page.locator(".chip.wait")).to_have_count(0, timeout=15000)
        await expect(page.locator("tbody.part")).to_have_count(1)
        await expect(page.locator("tbody.part .mpn")).to_have_text("LM358DT")
        await expect(page.locator(".banner", has_text="écartée")).to_be_visible()
        ok("recherche avancée")

        # ---- Panne d'un fournisseur ----
        await page.fill('input[aria-label="Recherche"]', "panne")
        await page.keyboard.press("Enter")
        await expect(page.locator(".chip.err", has_text="TME")).to_be_visible(timeout=15000)
        await expect(page.locator(".chip.wait")).to_have_count(0, timeout=15000)
        assert await page.locator("tbody.part").count() > 0
        assert "503" in (await page.locator(".chip.err").get_attribute("title"))
        await expect(page.locator(".chip-link")).to_have_attribute("href", "https://www.tme.eu/en/katalog/?search=panne")
        ok("une panne reste limitée à son fournisseur")

        # ---- Annulation ----
        await page.fill('input[aria-label="Recherche"]', "annulation")
        await page.keyboard.press("Enter")
        await page.click('.top button:has-text("Annuler")')
        await expect(page.locator('.top button:has-text("Rechercher")')).to_be_visible()
        ok("annulation d'une recherche")

        # ---- Code fournisseur ----
        await page.fill('input[aria-label="Recherche"]', "595-LM358DR")
        await page.click('.seg button:has-text("Code fournisseur")')
        await page.keyboard.press("Tab")
        await page.click('.top button:has-text("Rechercher")')
        await expect(page.locator(".banner", has_text="correspond à LM358DR")).to_be_visible(timeout=15000)
        ok("recherche par code fournisseur")
        await page.click('.seg button:has-text("Mots-clés")')

        # ---- Réglages ----
        await page.click('button[aria-label="Réglages"]')
        await expect(page.locator(".warn")).to_contain_text("Mode démo")
        # La barre « Enregistrer » touche le bas de l'écran : rien ne doit apparaître dessous.
        gap = await page.evaluate("innerHeight - document.querySelector('.sticky-save').getBoundingClientRect().bottom")
        assert abs(gap) <= 1, f"{gap} px sous la barre Enregistrer"
        await page.locator(".keycard", has_text="Mouser").locator('button:has-text("Tester")').click()
        await expect(page.locator(".keycard", has_text="Mouser").locator("[role=status]")).to_contain_text("Connexion réussie")
        await page.click("text=Ajouter un fournisseur")
        await page.fill('input[aria-label="Nom"]', "Abra")
        await page.fill('input[aria-label="Adresse"]', "javascript:alert(1)")
        await page.click('button:has-text("Enregistrer")')
        await expect(page.locator(".sticky-save [role=alert]")).to_contain_text("http(s)")
        await page.fill('input[aria-label="Adresse"]', "https://abra.test/s?q={0}")
        await page.click('button:has-text("Enregistrer")')
        await expect(page.locator(".sticky-save [role=status]")).to_contain_text("Enregistré")
        if SHOTS:
            await page.screenshot(path=SHOTS / "reglages.png")
        await page.keyboard.press("Escape")
        await expect(page.locator(".modal")).to_have_count(0)
        await page.fill('input[aria-label="Recherche"]', "NE555")
        await page.keyboard.press("Enter")
        await expect(page.locator(".chip.link", has_text="Abra")).to_have_attribute("href", "https://abra.test/s?q=NE555")
        ok("réglages : test de clé, fournisseur ajouté, adresse dangereuse refusée")

        # ---- Thème et mobile ----
        await expect(page.locator(".chip.wait")).to_have_count(0, timeout=15000)
        before_theme = await page.evaluate("document.documentElement.dataset.theme")
        await page.click('button[aria-label="Changer de thème"]')
        assert await page.evaluate("document.documentElement.dataset.theme") != before_theme
        if SHOTS:
            await page.screenshot(path=SHOTS / "autre-theme.png")
        await page.click('button[aria-label="Changer de thème"]')
        await page.set_viewport_size({"width": 390, "height": 800})
        await expect(page.locator("tbody.part").first.locator(".best")).to_be_visible()
        overflow = await page.evaluate("document.querySelector('.results').scrollWidth - document.querySelector('.results').clientWidth")
        assert overflow <= 1, f"débordement horizontal sur mobile : {overflow}px"
        await page.locator("tbody.part tr.head").first.click()
        await expect(page.locator("tbody.part.open button.add").first).to_be_visible()
        overflow = await page.evaluate("document.querySelector('.results').scrollWidth - document.querySelector('.results').clientWidth")
        assert overflow <= 1, f"débordement horizontal sur mobile, offres ouvertes : {overflow}px"
        await page.click('button:has-text("Filtres")')
        await expect(page.locator(".facets.show")).to_be_visible()
        await page.click('.facets button:has-text("Fermer")')
        await expect(page.locator(".facets.show")).to_have_count(0)
        if SHOTS:
            await page.screenshot(path=SHOTS / "mobile.png")
        await page.set_viewport_size({"width": 1440, "height": 900})
        ok("thème clair, mobile sans débordement")

        # ---- Déconnexion ----
        await page.click('button[aria-label="Réglages"]')
        await page.click("text=Se déconnecter")
        await expect(page.locator(".login form")).to_be_visible()
        await page.fill('input[type=password]', "faux-mot-de-passe")
        await page.click('button:has-text("Se connecter")')
        await expect(page.locator("[role=alert]")).to_contain_text("incorrect")
        await page.fill('input[type=password]', PASSWORD)
        await page.click('button:has-text("Se connecter")')
        await expect(page.locator(".top")).to_be_visible()
        ok("déconnexion, mauvais mot de passe, reconnexion")

        problems = [x for x in problems if "401" not in x and "status of 400" not in x]
        assert not problems, problems
        ok("aucune erreur JavaScript ni violation de la CSP")

        # ---- Anglais : la langue du navigateur décide, et les réglages peuvent la changer ----
        en = await browser.new_context(viewport={"width": 1440, "height": 900}, locale="en-US")
        page = await en.new_page()
        await page.goto(BASE)
        await page.fill('input[autocomplete="username"]', "admin")
        await page.fill('input[type=password]', "faux-mot-de-passe")
        await page.click('button:has-text("Sign in")')
        await expect(page.locator("[role=alert]")).to_have_text("Incorrect name or password.")
        # Écran de connexion : le bouton de langue s'applique tout de suite, et se retient sur cet appareil.
        await page.click('.login .lang-switch')
        await expect(page.locator('button:has-text("Se connecter")')).to_be_visible()
        await page.reload()
        await expect(page.locator('button:has-text("Se connecter")')).to_be_visible()
        await page.click('.login .lang-switch')
        await page.fill('input[autocomplete="username"]', "admin")
        await page.fill('input[type=password]', PASSWORD)
        await page.click('button:has-text("Sign in")')
        # Navigateur en anglais, compte réglé en français : c'est le compte qui décide.
        await expect(page.locator('.top button:has-text("Rechercher")')).to_be_visible()
        # Le bouton du haut change la langue, pour ce compte et sans passer par les réglages.
        await expect(page.locator(".top .lang-switch")).to_have_text("EN")
        high = await page.evaluate("document.querySelector('header.top').getBoundingClientRect().height")
        assert high < 70, f"barre du haut sur deux lignes à 1440 px en français : {high}"
        await page.click(".top .lang-switch")
        await expect(page.locator('.top button:has-text("Search")')).to_be_visible()
        await expect(page.locator(".top .lang-switch")).to_have_text("FR")
        high = await page.evaluate("document.querySelector('header.top').getBoundingClientRect().height")
        assert high < 70, f"barre du haut sur deux lignes à 1440 px en anglais : {high}"
        await page.fill('input[aria-label="Search"]', "LM358")
        await page.keyboard.press("Enter")
        await expect(page.locator(".foot")).to_contain_text("Finished", timeout=15000)
        await expect(page.locator(".chip.wait")).to_have_count(0, timeout=15000)
        first = page.locator("tbody.part", has_text="LM358DR").first
        await expect(first.locator(".col-stock")).to_have_text("84,407")
        await first.locator("tr.head").click()
        await page.click('.top button:has-text("BOM")')
        french = re.compile(r"Rechercher|Réglages|ournisseur|Mots-clés|Boîtier|Meilleur|Filtrer|Enregistrer|Fermer|déconnecter|Chargement|Paliers|Exporter|Avancée|pièce|offre\(s\)|Cycle de vie")
        left = french.findall(await page.locator("body").inner_text())
        assert not left, f"français dans l'interface anglaise : {left}"
        await page.click('.top button:has-text("BOM")')
        await page.click("text=Advanced")
        left = french.findall(await page.locator(".modal").inner_text())
        assert not left, f"français dans la recherche avancée : {left}"
        await page.keyboard.press("Escape")
        await page.click('button[aria-label="Settings"]')
        await expect(page.locator(".keycard", has_text="Mouser").locator(".sub.small")).to_contain_text("Search API")
        left = french.findall(await page.locator(".modal").inner_text())
        assert not left, f"français dans les réglages : {left}"
        await page.locator(".keycard", has_text="Mouser").locator('button:has-text("Test")').click()
        await expect(page.locator(".keycard", has_text="Mouser").locator("[role=status]")).to_contain_text("Connection OK")
        await page.select_option('select[aria-label="Language"]', "fr")
        await expect(page.locator('.top button:has-text("Rechercher")')).to_be_visible()
        assert await page.evaluate("document.documentElement.lang") == "fr"
        await en.close()
        ok("interface en anglais, sans français restant, et changement de langue")

        # ---- Plusieurs utilisateurs ----
        # L'administrateur reprend sa session en français, avec ses BOM et son historique.
        ctx_admin = await browser.new_context(viewport={"width": 1440, "height": 900}, locale="fr-CA")
        admin = await ctx_admin.new_page()
        await admin.goto(BASE)
        await admin.fill('input[autocomplete="username"]', "admin")
        await admin.fill('input[type=password]', PASSWORD)
        await admin.click('button:has-text("Se connecter")')
        await expect(admin.locator(".top")).to_be_visible()
        await admin.fill('input[aria-label="Recherche"]', "NE555-ADMIN")
        await admin.keyboard.press("Enter")
        await expect(admin.locator(".chip.wait")).to_have_count(0, timeout=15000)
        await admin.click('button[aria-label="Réglages"]')
        section = admin.locator("section", has=admin.locator('h3:has-text("Utilisateurs")'))
        await section.locator('button:has-text("Créer le compte")').scroll_into_view_if_needed()
        await section.get_by_label("Nom d'utilisateur").fill("bob")
        await section.get_by_label("Mot de passe").fill("court")
        await section.get_by_label("Mot de passe").fill("mot-de-passe-de-bob")
        await section.get_by_label("Langue").select_option("fr")
        await section.locator('button:has-text("Créer le compte")').click()
        await expect(section.locator("[role=status]")).to_contain_text("bob")
        await expect(section.locator(".keycard", has_text="bob")).to_be_visible()

        # Bob, navigateur en français : sa langue passe à l'anglais et le suit ; rien de l'administrateur n'apparaît.
        ctx_bob = await browser.new_context(viewport={"width": 1440, "height": 900}, locale="fr-CA")
        bob = await ctx_bob.new_page()
        await bob.goto(BASE)
        await bob.fill('input[autocomplete="username"]', "bob")
        await bob.fill('input[type=password]', "mot-de-passe-de-bob")
        await bob.click('button:has-text("Se connecter")')
        await expect(bob.locator(".welcome h1")).to_be_visible()
        await expect(bob.locator(".recent")).to_have_count(0)
        await expect(bob.locator(".top .count")).to_have_text("0")
        await bob.click('button[aria-label="Réglages"]')
        await expect(bob.locator('h3:has-text("Mon compte")')).to_be_visible()
        await expect(bob.locator('h3:has-text("Utilisateurs")')).to_have_count(0)
        await bob.select_option('select[aria-label="Langue"]', "en")
        await expect(bob.locator('.top button:has-text("Search")')).to_be_visible()
        ctx_bob2 = await browser.new_context(viewport={"width": 1440, "height": 900}, locale="fr-CA")
        bob2 = await ctx_bob2.new_page()
        await bob2.goto(BASE)
        await bob2.fill('input[autocomplete="username"]', "bob")
        await bob2.fill('input[type=password]', "mot-de-passe-de-bob")
        await bob2.click('button:has-text("Se connecter")')
        await expect(bob2.locator('.top button:has-text("Search")')).to_be_visible()
        await ctx_bob2.close()
        ok("un compte créé par l'administrateur : données séparées, langue propre")

        # Suppression : la session de Bob tombe dès sa requête suivante.
        admin.on("dialog", lambda d: asyncio.ensure_future(d.accept()))
        await section.locator(".keycard", has_text="bob").locator('button:has-text("Supprimer")').click()
        await expect(section.locator(".keycard", has_text="bob")).to_have_count(0)
        await bob.fill('input[aria-label="Search"]', "LM358")
        await bob.keyboard.press("Enter")
        await expect(bob.locator(".login form")).to_be_visible()
        await ctx_bob.close()
        await ctx_admin.close()
        ok("compte supprimé : session coupée")
        await browser.close()
    print(f"\n{len(passed)} parcours réussis")


asyncio.run(main())
