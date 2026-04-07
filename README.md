Skip to content
Suomen-sadehoito
EQD2Viewer
Repository navigation
Code
Issues
Pull requests
Agents
Actions
Projects
Wiki
Security and quality
Insights
Settings
Owner avatar
EQD2Viewer
Public
Suomen-sadehoito/EQD2Viewer
refactor/clean-architecture had recent pushes25 minutes ago
Go to file

README

MIT license

EQD2 Viewer
ESAPI-skripti Varian Eclipseen EQD2 jakaumien tarkasteluun ja uudelleensäteilytyksen kumulatiivisen annosjakauman arviointiin.

Projektin tila
Alpha Perustoiminnallisuus on kasassa mutta testausta kliinisillä potilailla ei ole vielä tehty riittävästi. Käytä omalla vastuulla ja tarkista aina laskelmat käsin.

Vaatimukset
Eclipse + ESAPI v15.6 tai uudempi
.NET Framework4.8

Kääntäminen
Avaa `ESAPI_EQD2Viewer.sln` Visual Studiossa. Käännä **Release|x64**. Costura.Fody pakkaa kaikki riippuvuudet yhteen DLL:ään.

Käyttö
1. Avaa potilas ja hoitosuunnitelma Eclipsessä
2. Aja skripti

Versiohistoria

Versio	Päivämäärä	Kuvaus
0.3.0-alpha	2026-03	Automaattinen .esapi.dll-päätteen lisääminen käännösvaiheessa (Assembly Name -päivitys projektitiedostoon).
0.2.0-alpha	2026-03	Yksikkötestit (107 kpl), ESAPI-stub-kirjasto CI-kääntämiseen, GitHub Actions -pipeline.
0.1.0-alpha	2026-03	Ensimmäinen alpha. CT/annos-näyttö, isodoosit, EQD2-muunnos, summaatio, DVH, rakennekohtainen α/β.
0.9.0-beta	2026-03	Previous beta release. (existing release in repository.)
0.9.1-beta	2026-04	Release for refactor/clean-architecture.

Tekijät
Risto Hirvilammi & Juho Ala-Myllymäki, ÖVPH

Lisenssi
MIT — ks. LICENSE.txt

About
No description, website, or topics provided.

Resources
 Readme
License
 MIT license
 Activity
 Custom properties
Stars
1 star
Watchers
0 watching
Forks
0 forks
 Audit log
Report repository
Releases4
v0.9.0-beta
Latest
2 weeks ago
+3 releases
Packages
No packages published
Publish your first package
Contributors
2
@RisDicom
RisDicom
@Copilot
Copilot
Languages
C#
100.0%
Footer
©2026 GitHub, Inc.
Footer navigation
Terms
Privacy
Security
Status
Community
Docs
Contact
Manage cookies
Do not share my personal information
