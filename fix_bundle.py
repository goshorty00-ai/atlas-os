import re

# Read the file
with open(r"Figma\Email\dist\assets\index-C4iwRHDc.js", "r", encoding="utf-8", errors="replace") as f:
    content = f.read()

print(f"Original length: {len(content)}")

# Remove the _html variable definition
content = re.sub(r',_html=\(typeof _d\.htmlBody==="string"\?_d\.htmlBody:""\)', '', content, count=1)
print("Removed _html definition")

# The conditional is: _html?o.jsx(...):o.jsx(...)
# We want to replace it with just: o.jsx(...) for the plaintext version

if "_html?" in content:
    # Find the section around _html?
    pos = content.find("_html?")
    before = content[:pos]
    after = content[pos+6:]  # Skip "_html?"
    
    # The pattern is: _html?[true case]:[false case],
    # true case: o.jsx("div",{...,dangerouslySetInnerHTML:{__html:_html}})
    # false case: o.jsx("div",{...,children:_bodyParas.map(...)})
    # We need to find where the false case ends (before the next comma)
    
    # Find the true case end (looks for }),: or just })
    match_true_start = 0
    bracket_count = 0
    in_true = True
    ternary_end = -1
    
    for i, char in enumerate(after):
        if char == '{' or char == '[' or char == '(':
            bracket_count += 1
        elif char == '}' or char == ']' or char == ')':
            bracket_count -= 1
        
        # After opening the true case, check for the colon
        if bracket_count == 0 and char == ':' and i > 10:
            in_true = False
            i_after_colon = i + 1
            # Now find the end of the false case
            bracket_count2 = 0
            for j in range(i_after_colon, len(after)):
                if after[j] in '({[':
                    bracket_count2 += 1
                elif after[j] in ')}]':
                    bracket_count2 -= 1
                    if bracket_count2 == -1:
                        ternary_end = j
                        break
                elif after[j] == ',' and bracket_count2 == 0:
                    ternary_end = j - 1
                    break
            break
    
    if ternary_end > 0:
        # Extract just the false case (plaintext rendering)
        false_case_start = after.find("o.jsx(\"div\",{className:\"pl-5") 
        if false_case_start > 0:
            # Find this after the colon
            false_case_content = after[false_case_start:ternary_end+1]
            new_content = before + false_case_content + after[ternary_end+1:]
            content = new_content
            print(f"Replaced conditional ternary with plaintext case. New length: {len(content)}")
    else:
        print("Could not find ternary end")
        print(f"Fragment around _html: {after[:300]}")
else:
    print("_html? not found")

print(f"Final length: {len(content)}")

# Write back
with open(r"Figma\Email\dist\assets\index-C4iwRHDc.js", "w", encoding="utf-8") as f:
    f.write(content)

print("File updated")
