// Match the app's SVG icon system instead of platform-dependent text glyphs.
export function rotationGrip() {
    const mark = document.createElement('span'); mark.setAttribute('aria-hidden', 'true');
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    for (const [name,value] of Object.entries({viewBox:'0 0 24 24',fill:'none',stroke:'currentColor','stroke-width':'1.8','stroke-linecap':'round','stroke-linejoin':'round',focusable:'false'})) svg.setAttribute(name,value);
    const path = document.createElementNS(svg.namespaceURI, 'path');
    path.setAttribute('d','M3 10a9 9 0 1 1 2 8 M3 4v6h6'); svg.append(path); mark.append(svg);
    return mark;
}
