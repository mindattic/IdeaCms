"""Generates a brochure body for every projects/* page.

The tagline is LIFTED from each project's own README rather than invented: the READMEs were seeded
from GitHub, so they are the project's own words and stay true when the repo changes. Anything the
README cannot supply (status, tech) is left off rather than guessed.
"""
import io, json, re, os

HERE = os.path.dirname(os.path.abspath(__file__))
pages = json.load(io.open(os.path.join(HERE, 'pages.clean.json'), encoding='utf-8'))
placeholder = io.open(os.path.join(HERE, 'placeholder.txt'), encoding='utf-8').read().strip()

# slug -> media uid for the projects that have been photographed
SHOTS = {
    'projects/hyperspace':            '1787c0d0-3e6d-4ca0-a28f-d125ef12ee4e',
    'projects/experimentrts':         '8177b945-5385-4864-af56-38d1e28f5c5c',
    'projects/prose':                 '4cb82c62-cde6-4ebe-b714-fcf33d4b525e',
    'projects/gridgame2026':          '4ceda94b-2a9d-4cfc-a4df-f5e5adf702eb',
    'projects/idiotproof':            'd2c1ce8a-59fd-4941-8248-c3fd392a3e2d',
    'projects/joycefit':              '809a5f06-dacc-4eb8-bf9b-1f08834b3579',
    'projects/mediabutler':           '5fab8ad2-5c88-46a2-b85b-073939ef9c30',
    'projects/mindattic-com':         '8ecc262a-8d75-46b2-aaaa-35fa6efd0270',
    'projects/mindatticcares-com':    '6bf1bfc1-5bdf-4208-b539-6002b9a53426',
    'projects/ryandebraal-com':       '0732f5f1-4c18-47c4-8af9-209e0e7817f5',
    'projects/audible-to-goodreads':  '3417263c-f631-4b5a-865a-7d96ac9b6fed',
    'projects/experimenteve':         '7ab8c5af-2d3f-4278-8cf8-c78db4840f13',
    'projects/mindattic-legion':      '4e05e4e1-4566-486c-a060-9c67eafcab7c',
    'projects/mindattic-psst':        '5a806ca2-645d-47bf-95a4-154829d61024',
    'projects/mindattic-uiux':        '5b717a1c-ef1b-4fb1-8c2c-67ee95906c71',
    'projects/taxratecollector':      '6d5db75c-ac53-45c5-a9d3-5b11fb7f4316',
    'projects/thinktank':             '8563d63b-a534-4599-a264-5f895dd5cbc7',
    'projects/tutor':                 '5b5e79af-1575-4eb2-a336-7fe2445b6945',
}
# a rendered diagram rather than a screenshot: wide, and it needs the full width
DIAGRAMS = {'projects/mindattic-vault': 'ccbf9a09-aa32-4b36-a70c-590e14ed43fd'}

# something you can actually run, from this page
LAUNCH = {
    'projects/hyperspace':    ('/hyperspace', 'Open Hyperspace'),
    'projects/experimentrts': ('/_ideas/Component/experimentrts/1/index.html', 'Play'),
}

# slug -> the GitHub repo name (defaults to a de-slugged guess)
REPO_OVERRIDE = {
    'projects/mindattic-com': 'mindattic.com',
    'projects/mindatticcares-com': 'mindatticcares.com',
    'projects/ryandebraal-com': 'ryandebraal.com',
    'projects/mindattic-ideas-library': 'IdeaCms.Library',
}

def repo_name(slug, title):
    if slug in REPO_OVERRIDE:
        return REPO_OVERRIDE[slug]
    tail = slug.split('/', 1)[1]
    if tail.startswith('mindattic-'):
        return 'MindAttic.' + ''.join(w.capitalize() for w in tail[len('mindattic-'):].split('-'))
    return title if title and ' ' not in title else tail

def strip_md(t):
    t = re.sub(r'!\[[^\]]*\]\([^)]*\)', '', t)              # images
    t = re.sub(r'\[([^\]]*)\]\([^)]*\)', r'\1', t)          # links -> text
    t = re.sub(r'`{1,3}([^`]*)`{1,3}', r'\1', t)            # code
    t = re.sub(r'[*_]{1,3}([^*_]+)[*_]{1,3}', r'\1', t)     # emphasis
    t = re.sub(r'<[^>]+>', '', t)                           # stray html
    t = re.sub(r'\s+', ' ', t)
    return t.strip()

def tagline_from(md):
    """First real sentence of the README — its own claim about itself."""
    if not md:
        return None
    body = md.split('\n')
    out = []
    for line in body:
        s = line.strip()
        if not s or s.startswith('#') or s.startswith('>') or s.startswith('!['):
            if out:
                break
            continue
        if s.startswith('|') or s.startswith('---') or s.startswith('```'):
            if out:
                break
            continue
        out.append(s)
        if len(' '.join(out)) > 150:
            break
    text = strip_md(' '.join(out))
    if not text:
        return None
    # GitHub's own stub for an empty repo is not a claim about the project — quoting it as the
    # tagline puts "This repository does not have a README yet." under the title as if it meant
    # something. Better to show no tagline at all.
    STUBS = ('this repository does not have a readme',
             'no description', 'description goes here', 'todo', 'coming soon')
    low = text.lower()
    if any(low.startswith(x) for x in STUBS):
        return None
    # one sentence, and never a run-on
    m = re.match(r'^(.{40,240}?[.!?])(\s|$)', text)
    text = m.group(1) if m else (text[:200].rsplit(' ', 1)[0] + '…' if len(text) > 200 else text)
    return text

def esc(s):
    return (s or '').replace("'", "''")

rows = []
for p in pages:
    slug, title = p['Slug'], (p.get('Title') or '').strip()
    md = ''
    if p.get('Meta'):
        try:
            md = json.loads(p['Meta']).get('markdown', '') or ''
        except Exception:
            md = ''

    heading = re.search(r'^#\s+(.+)$', md, re.M)
    display = strip_md(heading.group(1)) if heading else (title or slug.split('/')[-1])
    tagline = tagline_from(md)

    hero = SHOTS.get(slug) or DIAGRAMS.get(slug) or placeholder
    wide = ' widehero="true"' if slug in DIAGRAMS else ''
    if slug in DIAGRAMS:
        caption = 'How it works. Rendered from source, not drawn by hand.'
    elif slug in SHOTS:
        caption = f'{display}, captured running.'
    else:
        caption = 'Not photographed yet — this page is waiting on a capture.'

    repo = repo_name(slug, display)
    body = [
        '<Component.ProjectBrochure',
        f'    title="{display}"',
    ]
    if tagline:
        body.append(f'    tagline="{tagline}"')
    body += [
        f'    herouid="{hero}"{wide}',
        f'    caption="{caption}"',
        f'    repo="https://github.com/mindattic/{repo}">',
    ]
    if slug in LAUNCH:
        url, label = LAUNCH[slug]
        body.append(f'  <Component.AppLaunch url="{url}" title="{display}" '
                    f'buttontext="{label}" mode="fullscreen" />')
    body.append('  <Component.FromMd />')
    body.append('</Component.ProjectBrochure>')

    rows.append((slug, '\n'.join(body), bool(tagline), slug in SHOTS or slug in DIAGRAMS))

sql = ["SET QUOTED_IDENTIFIER ON;", "SET ANSI_NULLS ON;", "SET NOCOUNT ON;",
       "DECLARE @site int = (SELECT TOP 1 Id FROM Sites ORDER BY CASE WHEN IsDefault=1 THEN 0 ELSE 1 END, Id);",
       "DECLARE @n int = 0;", ""]
for slug, body, _, _ in rows:
    sql.append(f"UPDATE Pages SET BodyHtml = N'{esc(body)}', BodyTrust='Author', ModifiedUtc=SYSUTCDATETIME() "
               f"WHERE SiteId=@site AND Slug='{esc(slug)}';")
    sql.append("SET @n = @n + @@ROWCOUNT;")
sql.append("SELECT 'pages updated = ' + CAST(@n AS varchar(10));")

out = os.path.join(HERE, 'brochures-all.sql')
io.open(out, 'w', encoding='utf-8-sig').write('\n'.join(sql) + '\n')

have_tag = sum(1 for r in rows if r[2])
have_img = sum(1 for r in rows if r[3])
print(f"{len(rows)} pages | {have_tag} with a README-derived tagline | "
      f"{have_img} with a real image, {len(rows)-have_img} on the placeholder")
print("->", out)
for slug, _, t, i in rows[:6]:
    print(f"   {slug:<38} tagline={'yes' if t else 'NO '} image={'real' if i else 'placeholder'}")
