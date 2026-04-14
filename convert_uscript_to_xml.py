#!/usr/bin/env python3
"""
convert_uscript_to_xml.py
Converts the MINEMADE_CRAFTING.uscript recipe data into a
Rocket MinemadeCrafting.configuration.xml file.

Usage:
    python3 convert_uscript_to_xml.py MINEMADE_CRAFTING.uscript
Output:
    MinemadeCrafting.configuration.xml  (in the current directory)
"""

import sys
import re
import json
import xml.etree.ElementTree as ET
from xml.dom import minidom

def extract_json_config(raw: str) -> str:
    """
    The file starts with:
        RecipesConfig = [
            ...
        ];
    We grab everything between the first '[' and the matching ']'.
    Comments (//) are stripped first.
    """
    # Preserve https:// and http:// in URLs before stripping comments
    protected = re.sub(r'(https?:)(//)([^\s"]+)', r'\1SLASHSLASH\3', raw)
    no_comments = re.sub(r'//[^\n]*', '', protected)
    no_comments = no_comments.replace('SLASHSLASH', '//')

    start = no_comments.index('[')
    depth = 0
    end   = start
    for i, ch in enumerate(no_comments[start:], start):
        if ch == '[': depth += 1
        elif ch == ']':
            depth -= 1
            if depth == 0:
                end = i
                break

    return no_comments[start:end + 1]


def prettify(element: ET.Element) -> str:
    rough = ET.tostring(element, encoding='unicode')
    reparsed = minidom.parseString(rough)
    return reparsed.toprettyxml(indent='  ')


def build_xml(barricade_list: list) -> str:
    root = ET.Element('MinemadeCraftingConfig')
    recipes_node = ET.SubElement(root, 'BarricadeRecipes')

    for bc in barricade_list:
        bc_node = ET.SubElement(recipes_node, 'BarricadeConfig',
                                attrib={'barricadeId': str(bc['barricadeId'])})
        rlist   = ET.SubElement(bc_node, 'Recipes')

        for recipe in bc.get('recipes', []):
            r_node = ET.SubElement(rlist, 'Recipe',
                                   attrib={'id': str(recipe.get('id', 0))})
            ET.SubElement(r_node, 'Name').text        = recipe.get('name', '')
            ET.SubElement(r_node, 'Image').text       = recipe.get('image', '')
            ET.SubElement(r_node, 'CraftTime').text   = str(recipe.get('craftTime', 1))
            ET.SubElement(r_node, 'Permission').text  = recipe.get('permission', 'Default.craft.permission')
            ET.SubElement(r_node, 'NoPermission').text = recipe.get('noPermission', 'No permission.')

            items_node = ET.SubElement(r_node, 'RequiredItems')
            for item in recipe.get('requiredItems', []):
                ET.SubElement(items_node, 'Item',
                              attrib={
                                  'itemId':        str(item['itemId']),
                                  'amount':        str(item.get('amount', 1)),
                                  'needToBeRemoved': str(item.get('needToBeRemoved', True)).lower()
                              })

            cmds_node = ET.SubElement(r_node, 'RewardCommands')
            for cmd in recipe.get('rewardCommands', []):
                text       = cmd[0] if isinstance(cmd, list) else cmd.get('command', '')
                is_server  = str(cmd[1]).lower() if isinstance(cmd, list) else str(cmd.get('isServer', False)).lower()
                ET.SubElement(cmds_node, 'Cmd',
                              attrib={'isServer': is_server}).text = text

    return prettify(root)


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else 'MINEMADE_CRAFTING.uscript'

    with open(path, 'r', encoding='utf-8') as f:
        raw = f.read()

    print(f"Read {len(raw):,} bytes from {path}")

    json_str = extract_json_config(raw)
    print(f"Extracted JSON block: {len(json_str):,} chars")

    # Fix trailing commas before } or ] (common in uScript JSON-like syntax)
    json_str = re.sub(r',\s*([}\]])', r'\1', json_str)

    data = json.loads(json_str)
    print(f"Parsed {len(data)} barricade config(s)")

    xml_str = build_xml(data)

    out_path = 'MinemadeCrafting.configuration.xml'
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write(xml_str)

    total_recipes = sum(len(bc.get('recipes', [])) for bc in data)
    print(f"Written {out_path}  ({total_recipes} total recipes across {len(data)} barricades)")


if __name__ == '__main__':
    main()
