// Only visible labels are localized. Exceptions and diagnostics stay English.
export function editorLabel(element,key,...values){
    let labels={};try{labels=JSON.parse(element.closest('[data-editor-labels]')?.dataset.editorLabels||'{}');}catch{}
    return (labels[key]||key).replace(/\{(\d+)\}/g,(match,index)=>values[Number(index)]??match);
}
