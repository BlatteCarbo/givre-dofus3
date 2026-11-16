-- Bastionbot | Bucheron auto revise | 2026-10-26
-- Niveau métier lu automatiquement via getJobLevel(jobId).
-- JOB_LEVEL_OVERRIDE > 0 permet seulement un test manuel ponctuel.
MAX_PODS = 85
OPEN_BAGS = false
MIN_LIFE_PERCENT = 0
ITEMS_TO_GATHER = {303,449,460,461,470,471,472,473,474,476,2357,2358,7013,7014,7016,7925,11107,16488,17991,27375}
ELEMENTS_TO_GATHER = {}
MIN_MONSTERS = 1
MAX_MONSTERS = 1 -- aucun combat volontaire ; protecteurs geres par le moteur
JOB_LEVEL_OVERRIDE = 0
BANK_NAME = "Astrub"
ROUTE_ROTATION = true
ROUTES_IN_ROTATION = 3
LOOPS_BEFORE_ROTATE = 2
ROUTE_COOLDOWN_SECONDS = 120
FAILURE_PAUSE_SECONDS = 60

local JOB_ID = 2
local BANKS = {
  ["Astrub"] = {outside=191104002, inside=192415750, door=288},
  ["Bonta"] = {outside=212600322, inside=217059328, door=468},
  ["Brakmar"] = {outside=212861954, inside=214696968, door=243},
  ["Frigost"] = {outside=54172457, inside=54534165, door=358},
  ["Sufokia"] = {outside=90703872, inside=91753985, door=302},
}

local PALIERS = {
  [1] = { entry=88082192, route={
    {pos="4,7", dir="left", nextMap=88081680, edgeCell=252, edgeType=1, gather=true},
      {pos="3,7", dir="bottom", nextMap=88081679, edgeCell=539, edgeType=1, gather=true},
      {pos="3,8", dir="bottom", nextMap=88081678, edgeCell=539, edgeType=1, gather=true},
      {pos="3,9", dir="left", nextMap=88081166, edgeCell=252, edgeType=1, gather=true},
      {pos="2,9", dir="left", nextMap=88080654, edgeCell=252, edgeType=1, gather=true},
      {pos="1,9", dir="left", nextMap=88212238, edgeCell=252, edgeType=1, gather=true},
      {pos="0,9", dir="top", nextMap=88212239, edgeCell=6, edgeType=1, gather=true},
      {pos="0,8", dir="right", nextMap=88080655, edgeCell=279, edgeType=1, gather=true},
      {pos="1,8", dir="right", nextMap=88081167, edgeCell=237, edgeType=1, gather=true},
      {pos="2,8", dir="right", nextMap=88081679, edgeCell=279, edgeType=1, gather=true},
      {pos="3,8", dir="right", nextMap=88082191, edgeCell=405, edgeType=1, gather=true},
      {pos="4,8", dir="right", nextMap=88082703, edgeCell=279, edgeType=1, gather=true},
      {pos="5,8", dir="top", nextMap=88082704, edgeCell=6, edgeType=1, gather=true},
      {pos="5,7", dir="left", nextMap=88082192, edgeCell=252, edgeType=1, gather=true},
      {pos="4,7", dir="bottom", nextMap=88082191, edgeCell=539, edgeType=1, gather=true},
      {pos="4,8", dir="bottom", nextMap=88082190, edgeCell=539, edgeType=1, gather=true},
      {pos="4,9", dir="left", nextMap=88081678, edgeCell=252, edgeType=1, gather=true},
      {pos="3,9", dir="left", nextMap=88081166, edgeCell=252, edgeType=1, gather=true},
      {pos="2,9", dir="left", nextMap=88080654, edgeCell=252, edgeType=1, gather=true},
      {pos="1,9", dir="left", nextMap=88212238, edgeCell=252, edgeType=1, gather=true},
      {pos="0,9", dir="top", nextMap=88212239, edgeCell=6, edgeType=1, gather=true},
      {pos="0,8", dir="top", nextMap=88212240, edgeCell=7, edgeType=1, gather=true},
      {pos="0,7", dir="top", nextMap=88212241, edgeCell=2, edgeType=1, gather=true},
      {pos="0,6", dir="top", nextMap=88212242, edgeCell=1, edgeType=1, gather=true},
      {pos="0,5", dir="top", nextMap=88212243, edgeCell=6, edgeType=1, gather=true},
      {pos="0,4", dir="right", nextMap=88080659, edgeCell=279, edgeType=1, gather=true},
      {pos="1,4", dir="bottom", nextMap=88080658, edgeCell=539, edgeType=1, gather=true},
      {pos="1,5", dir="bottom", nextMap=88080657, edgeCell=544, edgeType=1, gather=true},
      {pos="1,6", dir="bottom", nextMap=88080656, edgeCell=542, edgeType=1, gather=true},
      {pos="1,7", dir="bottom", nextMap=88080655, edgeCell=538, edgeType=1, gather=true},
      {pos="1,8", dir="bottom", nextMap=88080654, edgeCell=539, edgeType=1, gather=true},
      {pos="1,9", dir="bottom", nextMap=88080653, edgeCell=539, edgeType=1, gather=true},
      {pos="1,10", dir="bottom", nextMap=88080652, edgeCell=539, edgeType=1, gather=true},
      {pos="1,11", dir="right", nextMap=88081164, edgeCell=279, edgeType=1, gather=true},
      {pos="2,11", dir="bottom", nextMap=88081163, edgeCell=539, edgeType=1, gather=true},
      {pos="2,12", dir="bottom", nextMap=88081162, edgeCell=539, edgeType=1, gather=true},
      {pos="2,13", dir="bottom", nextMap=88081161, edgeCell=539, edgeType=1, gather=true},
      {pos="2,14", dir="bottom", nextMap=88081160, edgeCell=539, edgeType=1, gather=true},
      {pos="2,15", dir="bottom", nextMap=88081159, edgeCell=539, edgeType=1, gather=true},
      {pos="2,16", dir="top", nextMap=88081160, edgeCell=6, edgeType=1, gather=true},
      {pos="2,15", dir="top", nextMap=88081161, edgeCell=6, edgeType=1, gather=true},
      {pos="2,14", dir="top", nextMap=88081162, edgeCell=6, edgeType=1, gather=true},
      {pos="2,13", dir="right", nextMap=88081674, edgeCell=265, edgeType=1, gather=true},
      {pos="3,13", dir="top", nextMap=88081675, edgeCell=6, edgeType=1, gather=true},
      {pos="3,12", dir="right", nextMap=88082187, edgeCell=279, edgeType=1, gather=true},
      {pos="4,12", dir="right", nextMap=88082699, edgeCell=279, edgeType=1, gather=true},
      {pos="5,12", dir="bottom", nextMap=88082698, edgeCell=539, edgeType=1, gather=true},
      {pos="5,13", dir="bottom", nextMap=88082697, edgeCell=539, edgeType=1, gather=true},
      {pos="5,14", dir="bottom", nextMap=88082696, edgeCell=539, edgeType=1, gather=true},
      {pos="5,15", dir="right", nextMap=88083208, edgeCell=279, edgeType=1, gather=true},
      {pos="6,15", dir="bottom", nextMap=88083207, edgeCell=539, edgeType=1, gather=true},
      {pos="6,16", dir="bottom", nextMap=88083206, edgeCell=539, edgeType=1, gather=true},
      {pos="6,17", dir="left", nextMap=88082694, edgeCell=252, edgeType=1, gather=true},
      {pos="5,17", dir="right", nextMap=88083206, edgeCell=279, edgeType=1, gather=true},
      {pos="6,17", dir="left", nextMap=88082694, edgeCell=252, edgeType=1, gather=true},
      {pos="5,17", dir="bottom", nextMap=88082693, edgeCell=539, edgeType=1, gather=true},
      {pos="5,18", dir="left", nextMap=88082181, edgeCell=224, edgeType=1, gather=true},
      {pos="4,18", dir="left", nextMap=88081669, edgeCell=252, edgeType=1, gather=true},
      {pos="3,18", dir="bottom", nextMap=88081668, edgeCell=539, edgeType=1, gather=true},
      {pos="3,19", dir="bottom", nextMap=88081667, edgeCell=539, edgeType=1, gather=true},
      {pos="3,20", dir="bottom", nextMap=88081666, edgeCell=539, edgeType=1, gather=true},
      {pos="3,21", dir="bottom", nextMap=88081665, edgeCell=538, edgeType=1, gather=true},
      {pos="3,22", dir="top", nextMap=88081666, edgeCell=6, edgeType=1, gather=true},
      {pos="3,21", dir="top", nextMap=88081667, edgeCell=6, edgeType=1, gather=true},
      {pos="3,20", dir="top", nextMap=88081668, edgeCell=6, edgeType=1, gather=true},
      {pos="3,19", dir="top", nextMap=88081669, edgeCell=6, edgeType=1, gather=true},
      {pos="3,18", dir="right", nextMap=88082181, edgeCell=279, edgeType=1, gather=true},
      {pos="4,18", dir="right", nextMap=88082693, edgeCell=251, edgeType=1, gather=true},
      {pos="5,18", dir="top", nextMap=88082694, edgeCell=6, edgeType=1, gather=true},
      {pos="5,17", dir="right", nextMap=88083206, edgeCell=279, edgeType=1, gather=true},
      {pos="6,17", dir="top", nextMap=88083207, edgeCell=6, edgeType=1, gather=true},
      {pos="6,16", dir="top", nextMap=88083208, edgeCell=6, edgeType=1, gather=true},
      {pos="6,15", dir="left", nextMap=88082696, edgeCell=252, edgeType=1, gather=true},
      {pos="5,15", dir="top", nextMap=88082697, edgeCell=6, edgeType=1, gather=true},
      {pos="5,14", dir="top", nextMap=88082698, edgeCell=6, edgeType=1, gather=true},
      {pos="5,13", dir="top", nextMap=88082699, edgeCell=6, edgeType=1, gather=true},
      {pos="5,12", dir="left", nextMap=88082187, edgeCell=252, edgeType=1, gather=true},
      {pos="4,12", dir="top", nextMap=88082188, edgeCell=6, edgeType=1, gather=true},
      {pos="4,11", dir="top", nextMap=88082189, edgeCell=6, edgeType=1, gather=true},
      {pos="4,10", dir="top", nextMap=88082190, edgeCell=6, edgeType=1, gather=true},
      {pos="4,9", dir="top", nextMap=88082191, edgeCell=6, edgeType=1, gather=true},
      {pos="4,8", dir="top", nextMap=88082192, edgeCell=6, edgeType=1, gather=true},
  }},
  [20] = { entry=88081415, route={
    {pos="3,30", dir="left", nextMap=88080903, edgeCell=252, edgeType=1, gather=true},
      {pos="2,30", dir="left", nextMap=88080391, edgeCell=280, edgeType=1, gather=true},
      {pos="1,30", dir="right", nextMap=88080903, edgeCell=293, edgeType=1, gather=true},
      {pos="2,30", dir="top", nextMap=88080902, edgeCell=6, edgeType=1, gather=true},
      {pos="2,29", dir="right", nextMap=88081414, edgeCell=377, edgeType=1, gather=true},
      {pos="3,29", dir="right", nextMap=88081926, edgeCell=279, edgeType=1, gather=true},
      {pos="4,29", dir="right", nextMap=88082438, edgeCell=279, edgeType=1, gather=true},
      {pos="5,29", dir="bottom", nextMap=88082439, edgeCell=539, edgeType=1, gather=true},
      {pos="5,30", dir="left", nextMap=88081927, edgeCell=252, edgeType=1, gather=true},
      {pos="4,30", dir="top", nextMap=88081926, edgeCell=6, edgeType=1, gather=true},
      {pos="4,29", dir="right", nextMap=88082438, edgeCell=279, edgeType=1, gather=true},
      {pos="5,29", dir="right", nextMap=88082950, edgeCell=265, edgeType=1, gather=true},
      {pos="6,29", dir="bottom", nextMap=88082951, edgeCell=539, edgeType=1, gather=true},
      {pos="6,30", dir="right", nextMap=88083463, edgeCell=279, edgeType=1, gather=true},
      {pos="7,30", dir="right", nextMap=88083975, edgeCell=279, edgeType=1, gather=true},
      {pos="8,30", dir="right", nextMap=88084487, edgeCell=279, edgeType=1, gather=true},
      {pos="9,30", dir="top", nextMap=88084486, edgeCell=6, edgeType=1, gather=true},
      {pos="9,29", dir="bottom", nextMap=88084487, edgeCell=539, edgeType=1, gather=true},
      {pos="9,30", dir="left", nextMap=88083975, edgeCell=252, edgeType=1, gather=true},
      {pos="8,30", dir="left", nextMap=88083463, edgeCell=252, edgeType=1, gather=true},
      {pos="7,30", dir="left", nextMap=88082951, edgeCell=252, edgeType=1, gather=true},
      {pos="6,30", dir="left", nextMap=88082439, edgeCell=252, edgeType=1, gather=true},
      {pos="5,30", dir="left", nextMap=88081927, edgeCell=252, edgeType=1, gather=true},
      {pos="4,30", dir="top", nextMap=88081926, edgeCell=6, edgeType=1, gather=true},
      {pos="4,29", dir="left", nextMap=88081414, edgeCell=252, edgeType=1, gather=true},
      {pos="3,29", dir="top", nextMap=88081413, edgeCell=8, edgeType=1, gather=true},
      {pos="3,28", dir="top", nextMap=88081412, edgeCell=5, edgeType=1, gather=true},
      {pos="3,27", dir="top", nextMap=88081411, edgeCell=6, edgeType=1, gather=true},
      {pos="3,26", dir="top", nextMap=88081410, edgeCell=6, edgeType=1, gather=true},
      {pos="3,25", dir="top", nextMap=88081409, edgeCell=6, edgeType=1, gather=true},
      {pos="3,24", dir="top", nextMap=88081408, edgeCell=6, edgeType=1, gather=true},
      {pos="3,23", dir="top", nextMap=88081665, edgeCell=6, edgeType=1, gather=true},
      {pos="3,22", dir="top", nextMap=88081666, edgeCell=6, edgeType=1, gather=true},
      {pos="3,21", dir="top", nextMap=88081667, edgeCell=6, edgeType=1, gather=true},
      {pos="3,20", dir="bottom", nextMap=88081666, edgeCell=539, edgeType=1, gather=true},
      {pos="3,21", dir="bottom", nextMap=88081665, edgeCell=538, edgeType=1, gather=true},
      {pos="3,22", dir="bottom", nextMap=88081408, edgeCell=539, edgeType=1, gather=true},
      {pos="3,23", dir="top", nextMap=88081665, edgeCell=6, edgeType=1, gather=true},
      {pos="3,22", dir="top", nextMap=88081666, edgeCell=6, edgeType=1, gather=true},
      {pos="3,21", dir="top", nextMap=88081667, edgeCell=6, edgeType=1, gather=true},
      {pos="3,20", dir="top", nextMap=88081668, edgeCell=6, edgeType=1, gather=true},
      {pos="3,19", dir="top", nextMap=88081669, edgeCell=6, edgeType=1, gather=true},
      {pos="3,18", dir="right", nextMap=88082181, edgeCell=279, edgeType=1, gather=true},
      {pos="4,18", dir="right", nextMap=88082693, edgeCell=251, edgeType=1, gather=true},
      {pos="5,18", dir="top", nextMap=88082694, edgeCell=6, edgeType=1, gather=true},
      {pos="5,17", dir="right", nextMap=88083206, edgeCell=279, edgeType=1, gather=true},
      {pos="6,17", dir="left", nextMap=88082694, edgeCell=252, edgeType=1, gather=true},
      {pos="5,17", dir="bottom", nextMap=88082693, edgeCell=539, edgeType=1, gather=true},
      {pos="5,18", dir="right", nextMap=88083205, edgeCell=279, edgeType=1, gather=true},
      {pos="6,18", dir="top", nextMap=88083206, edgeCell=6, edgeType=1, gather=true},
      {pos="6,17", dir="top", nextMap=88083207, edgeCell=6, edgeType=1, gather=true},
      {pos="6,16", dir="top", nextMap=88083208, edgeCell=6, edgeType=1, gather=true},
      {pos="6,15", dir="left", nextMap=88082696, edgeCell=252, edgeType=1, gather=true},
      {pos="5,15", dir="bottom", nextMap=88082695, edgeCell=539, edgeType=1, gather=true},
      {pos="5,16", dir="top", nextMap=88082696, edgeCell=7, edgeType=1, gather=true},
      {pos="5,15", dir="right", nextMap=88083208, edgeCell=279, edgeType=1, gather=true},
      {pos="6,15", dir="bottom", nextMap=88083207, edgeCell=539, edgeType=1, gather=true},
      {pos="6,16", dir="bottom", nextMap=88083206, edgeCell=539, edgeType=1, gather=true},
      {pos="6,17", dir="left", nextMap=88082694, edgeCell=252, edgeType=1, gather=true},
      {pos="5,17", dir="bottom", nextMap=88082693, edgeCell=539, edgeType=1, gather=true},
      {pos="5,18", dir="left", nextMap=88082181, edgeCell=224, edgeType=1, gather=true},
      {pos="4,18", dir="left", nextMap=88081669, edgeCell=252, edgeType=1, gather=true},
      {pos="3,18", dir="bottom", nextMap=88081668, edgeCell=539, edgeType=1, gather=true},
      {pos="3,19", dir="bottom", nextMap=88081667, edgeCell=539, edgeType=1, gather=true},
      {pos="3,20", dir="bottom", nextMap=88081666, edgeCell=539, edgeType=1, gather=true},
      {pos="3,21", dir="bottom", nextMap=88081665, edgeCell=538, edgeType=1, gather=true},
      {pos="3,22", dir="bottom", nextMap=88081408, edgeCell=539, edgeType=1, gather=true},
      {pos="3,23", dir="bottom", nextMap=88081409, edgeCell=538, edgeType=1, gather=true},
      {pos="3,24", dir="bottom", nextMap=88081410, edgeCell=539, edgeType=1, gather=true},
      {pos="3,25", dir="bottom", nextMap=88081411, edgeCell=539, edgeType=1, gather=true},
      {pos="3,26", dir="bottom", nextMap=88081412, edgeCell=538, edgeType=1, gather=true},
      {pos="3,27", dir="bottom", nextMap=88081413, edgeCell=538, edgeType=1, gather=true},
      {pos="3,28", dir="bottom", nextMap=88081414, edgeCell=540, edgeType=1, gather=true},
      {pos="3,29", dir="bottom", nextMap=88081415, edgeCell=539, edgeType=1, gather=true},
  }},
  [40] = { entry=88083463, route={
    {pos="7,30", dir="left", nextMap=88082951, edgeCell=252, edgeType=1, gather=true},
      {pos="6,30", dir="top", nextMap=88082950, edgeCell=6, edgeType=1, gather=true},
      {pos="6,29", dir="left", nextMap=88082438, edgeCell=252, edgeType=1, gather=true},
      {pos="5,29", dir="left", nextMap=88081926, edgeCell=252, edgeType=1, gather=true},
      {pos="4,29", dir="bottom", nextMap=88081927, edgeCell=539, edgeType=1, gather=true},
      {pos="4,30", dir="bottom", nextMap=88081928, edgeCell=539, edgeType=1, gather=true},
      {pos="4,31", dir="left", nextMap=88081416, edgeCell=252, edgeType=1, gather=true},
      {pos="3,31", dir="top", nextMap=88081415, edgeCell=7, edgeType=1, gather=true},
      {pos="3,30", dir="left", nextMap=88080903, edgeCell=252, edgeType=1, gather=true},
      {pos="2,30", dir="left", nextMap=88080391, edgeCell=280, edgeType=1, gather=true},
      {pos="1,30", dir="right", nextMap=88080903, edgeCell=293, edgeType=1, gather=true},
      {pos="2,30", dir="top", nextMap=88080902, edgeCell=6, edgeType=1, gather=true},
      {pos="2,29", dir="right", nextMap=88081414, edgeCell=377, edgeType=1, gather=true},
      {pos="3,29", dir="right", nextMap=88081926, edgeCell=279, edgeType=1, gather=true},
      {pos="4,29", dir="bottom", nextMap=88081927, edgeCell=539, edgeType=1, gather=true},
      {pos="4,30", dir="right", nextMap=88082439, edgeCell=279, edgeType=1, gather=true},
      {pos="5,30", dir="right", nextMap=88082951, edgeCell=279, edgeType=1, gather=true},
      {pos="6,30", dir="right", nextMap=88083463, edgeCell=279, edgeType=1, gather=true},
      {pos="7,30", dir="right", nextMap=88083975, edgeCell=279, edgeType=1, gather=true},
      {pos="8,30", dir="right", nextMap=88084487, edgeCell=279, edgeType=1, gather=true},
      {pos="9,30", dir="top", nextMap=88084486, edgeCell=6, edgeType=1, gather=true},
      {pos="9,29", dir="bottom", nextMap=88084487, edgeCell=539, edgeType=1, gather=true},
      {pos="9,30", dir="left", nextMap=88083975, edgeCell=252, edgeType=1, gather=true},
      {pos="8,30", dir="left", nextMap=88083463, edgeCell=252, edgeType=1, gather=true},
      {pos="7,30", dir="left", nextMap=88082951, edgeCell=252, edgeType=1, gather=true},
      {pos="6,30", dir="left", nextMap=88082439, edgeCell=252, edgeType=1, gather=true},
      {pos="5,30", dir="left", nextMap=88081927, edgeCell=252, edgeType=1, gather=true},
      {pos="4,30", dir="top", nextMap=88081926, edgeCell=6, edgeType=1, gather=true},
      {pos="4,29", dir="left", nextMap=88081414, edgeCell=252, edgeType=1, gather=true},
      {pos="3,29", dir="top", nextMap=88081413, edgeCell=8, edgeType=1, gather=true},
      {pos="3,28", dir="top", nextMap=88081412, edgeCell=5, edgeType=1, gather=true},
      {pos="3,27", dir="top", nextMap=88081411, edgeCell=6, edgeType=1, gather=true},
      {pos="3,26", dir="top", nextMap=88081410, edgeCell=6, edgeType=1, gather=true},
      {pos="3,25", dir="top", nextMap=88081409, edgeCell=6, edgeType=1, gather=true},
      {pos="3,24", dir="top", nextMap=88081408, edgeCell=6, edgeType=1, gather=true},
      {pos="3,23", dir="top", nextMap=88081665, edgeCell=6, edgeType=1, gather=true},
      {pos="3,22", dir="top", nextMap=88081666, edgeCell=6, edgeType=1, gather=true},
      {pos="3,21", dir="top", nextMap=88081667, edgeCell=6, edgeType=1, gather=true},
      {pos="3,20", dir="bottom", nextMap=88081666, edgeCell=539, edgeType=1, gather=true},
      {pos="3,21", dir="bottom", nextMap=88081665, edgeCell=538, edgeType=1, gather=true},
      {pos="3,22", dir="bottom", nextMap=88081408, edgeCell=539, edgeType=1, gather=true},
      {pos="3,23", dir="top", nextMap=88081665, edgeCell=6, edgeType=1, gather=true},
      {pos="3,22", dir="top", nextMap=88081666, edgeCell=6, edgeType=1, gather=true},
      {pos="3,21", dir="top", nextMap=88081667, edgeCell=6, edgeType=1, gather=true},
      {pos="3,20", dir="top", nextMap=88081668, edgeCell=6, edgeType=1, gather=true},
      {pos="3,19", dir="top", nextMap=88081669, edgeCell=6, edgeType=1, gather=true},
      {pos="3,18", dir="right", nextMap=88082181, edgeCell=279, edgeType=1, gather=true},
      {pos="4,18", dir="right", nextMap=88082693, edgeCell=251, edgeType=1, gather=true},
      {pos="5,18", dir="left", nextMap=88082181, edgeCell=224, edgeType=1, gather=true},
      {pos="4,18", dir="bottom", nextMap=88082180, edgeCell=536, edgeType=1, gather=true},
      {pos="4,19", dir="bottom", nextMap=88082179, edgeCell=540, edgeType=1, gather=true},
      {pos="4,20", dir="bottom", nextMap=88082178, edgeCell=541, edgeType=1, gather=true},
      {pos="4,21", dir="bottom", nextMap=88082177, edgeCell=539, edgeType=1, gather=true},
      {pos="4,22", dir="bottom", nextMap=88081920, edgeCell=539, edgeType=1, gather=true},
      {pos="4,23", dir="bottom", nextMap=88081921, edgeCell=537, edgeType=1, gather=true},
      {pos="4,24", dir="bottom", nextMap=88081922, edgeCell=550, edgeType=1, gather=true},
      {pos="4,25", dir="bottom", nextMap=88081923, edgeCell=538, edgeType=1, gather=true},
      {pos="4,26", dir="bottom", nextMap=88081924, edgeCell=539, edgeType=1, gather=true},
      {pos="4,27", dir="right", nextMap=88082436, edgeCell=279, edgeType=1, gather=true},
      {pos="5,27", dir="bottom", nextMap=88082437, edgeCell=536, edgeType=1, gather=true},
      {pos="5,28", dir="bottom", nextMap=88082438, edgeCell=539, edgeType=1, gather=true},
      {pos="5,29", dir="bottom", nextMap=88082439, edgeCell=539, edgeType=1, gather=true},
      {pos="5,30", dir="right", nextMap=88082951, edgeCell=279, edgeType=1, gather=true},
      {pos="6,30", dir="right", nextMap=88083463, edgeCell=279, edgeType=1, gather=true},
  }},
  [60] = { entry=155976713, route={
    {pos="-13,-50", dir="bottom", nextMap=155976714, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-49", dir="left", nextMap=155976202, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-49", dir="top", nextMap=155976201, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-50", dir="top", nextMap=155976200, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-51", dir="left", nextMap=155975688, edgeCell=140, edgeType=1, gather=true},
      {pos="-15,-51", dir="top", nextMap=155975687, edgeCell=8, edgeType=1, gather=true},
      {pos="-15,-52", dir="top", nextMap=155975686, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-53", dir="bottom", nextMap=155975687, edgeCell=553, edgeType=1, gather=true},
      {pos="-15,-52", dir="top", nextMap=155975686, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-53", dir="right", nextMap=155976198, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-53", dir="bottom", nextMap=155976199, edgeCell=552, edgeType=1, gather=true},
      {pos="-14,-52", dir="right", nextMap=155976711, edgeCell=363, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="bottom", nextMap=155976712, edgeCell=553, edgeType=1, gather=true},
      {pos="-13,-51", dir="bottom", nextMap=155976713, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-50", dir="left", nextMap=155976201, edgeCell=280, edgeType=1, gather=true},
      {pos="-14,-50", dir="left", nextMap=155975689, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-50", dir="bottom", nextMap=155975690, edgeCell=539, edgeType=1, gather=true},
      {pos="-15,-49", dir="bottom", nextMap=155975691, edgeCell=537, edgeType=1, gather=true},
      {pos="-15,-48", dir="bottom", nextMap=155975692, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-47", dir="bottom", nextMap=155975693, edgeCell=537, edgeType=1, gather=true},
      {pos="-15,-46", dir="right", nextMap=155976205, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-46", dir="top", nextMap=155976204, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-47", dir="left", nextMap=155975692, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-47", dir="top", nextMap=155975691, edgeCell=8, edgeType=1, gather=true},
      {pos="-15,-48", dir="top", nextMap=155975690, edgeCell=5, edgeType=1, gather=true},
      {pos="-15,-49", dir="top", nextMap=155975689, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-50", dir="right", nextMap=155976201, edgeCell=377, edgeType=1, gather=true},
      {pos="-14,-50", dir="bottom", nextMap=155976202, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-49", dir="right", nextMap=155976714, edgeCell=349, edgeType=1, gather=true},
      {pos="-13,-49", dir="bottom", nextMap=155976715, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="top", nextMap=155976713, edgeCell=8, edgeType=1, gather=true},
  }},
  [70] = { entry=155976713, route={
    {pos="-13,-50", dir="bottom", nextMap=155976714, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-49", dir="left", nextMap=155976202, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-49", dir="top", nextMap=155976201, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-50", dir="top", nextMap=155976200, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-51", dir="left", nextMap=155975688, edgeCell=140, edgeType=1, gather=true},
      {pos="-15,-51", dir="top", nextMap=155975687, edgeCell=8, edgeType=1, gather=true},
      {pos="-15,-52", dir="top", nextMap=155975686, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-53", dir="bottom", nextMap=155975687, edgeCell=553, edgeType=1, gather=true},
      {pos="-15,-52", dir="top", nextMap=155975686, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-53", dir="right", nextMap=155976198, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-53", dir="bottom", nextMap=155976199, edgeCell=552, edgeType=1, gather=true},
      {pos="-14,-52", dir="right", nextMap=155976711, edgeCell=363, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="bottom", nextMap=155976712, edgeCell=553, edgeType=1, gather=true},
      {pos="-13,-51", dir="bottom", nextMap=155976713, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-50", dir="left", nextMap=155976201, edgeCell=280, edgeType=1, gather=true},
      {pos="-14,-50", dir="left", nextMap=155975689, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-50", dir="bottom", nextMap=155975690, edgeCell=539, edgeType=1, gather=true},
      {pos="-15,-49", dir="bottom", nextMap=155975691, edgeCell=537, edgeType=1, gather=true},
      {pos="-15,-48", dir="bottom", nextMap=155975692, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-47", dir="bottom", nextMap=155975693, edgeCell=537, edgeType=1, gather=true},
      {pos="-15,-46", dir="right", nextMap=155976205, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-46", dir="top", nextMap=155976204, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-47", dir="left", nextMap=155975692, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-47", dir="top", nextMap=155975691, edgeCell=8, edgeType=1, gather=true},
      {pos="-15,-48", dir="top", nextMap=155975690, edgeCell=5, edgeType=1, gather=true},
      {pos="-15,-49", dir="top", nextMap=155975689, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-50", dir="right", nextMap=155976201, edgeCell=377, edgeType=1, gather=true},
      {pos="-14,-50", dir="bottom", nextMap=155976202, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-49", dir="right", nextMap=155976714, edgeCell=349, edgeType=1, gather=true},
      {pos="-13,-49", dir="bottom", nextMap=155976715, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="top", nextMap=155976713, edgeCell=8, edgeType=1, gather=true},
  }},
  [80] = { entry=155976713, route={
    {pos="-13,-50", dir="top", nextMap=155976712, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-51", dir="top", nextMap=155976711, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="right", nextMap=155977222, edgeCell=321, edgeType=1, gather=true},
      {pos="-12,-53", dir="top", nextMap=155977221, edgeCell=7, edgeType=1, gather=true},
      {pos="-12,-54", dir="bottom", nextMap=155977222, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-53", dir="left", nextMap=155976710, edgeCell=308, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="left", nextMap=155976199, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-52", dir="top", nextMap=155976198, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-53", dir="bottom", nextMap=155976199, edgeCell=552, edgeType=1, gather=true},
      {pos="-14,-52", dir="bottom", nextMap=155976200, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-51", dir="left", nextMap=155975688, edgeCell=140, edgeType=1, gather=true},
      {pos="-15,-51", dir="right", nextMap=155976200, edgeCell=153, edgeType=1, gather=true},
      {pos="-14,-51", dir="bottom", nextMap=155976201, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-50", dir="bottom", nextMap=155976202, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-49", dir="right", nextMap=155976714, edgeCell=349, edgeType=1, gather=true},
      {pos="-13,-49", dir="bottom", nextMap=155976715, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="bottom", nextMap=155976716, edgeCell=551, edgeType=1, gather=true},
      {pos="-13,-47", dir="bottom", nextMap=132121089, edgeCell=539, edgeType=2, gather=true},
      {pos="-13,-46", dir="top", nextMap=155976716, edgeCell=6, edgeType=2, gather=true},
      {pos="-13,-47", dir="right", nextMap=155977228, edgeCell=223, edgeType=1, gather=true},
      {pos="-12,-47", dir="top", nextMap=155977227, edgeCell=7, edgeType=1, gather=true},
      {pos="-12,-48", dir="right", nextMap=128059916, edgeCell=293, edgeType=2, gather=true},
      {pos="-11,-48", dir="left", nextMap=155977227, edgeCell=280, edgeType=2, gather=true},
      {pos="-12,-48", dir="left", nextMap=155976715, edgeCell=168, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="top", nextMap=155976713, edgeCell=8, edgeType=1, gather=true},
  }},
  [90] = { entry=155976713, route={
    {pos="-13,-50", dir="top", nextMap=155976712, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-51", dir="top", nextMap=155976711, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="right", nextMap=155977222, edgeCell=321, edgeType=1, gather=true},
      {pos="-12,-53", dir="top", nextMap=155977221, edgeCell=7, edgeType=1, gather=true},
      {pos="-12,-54", dir="bottom", nextMap=155977222, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-53", dir="left", nextMap=155976710, edgeCell=308, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="left", nextMap=155976199, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-52", dir="top", nextMap=155976198, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-53", dir="bottom", nextMap=155976199, edgeCell=552, edgeType=1, gather=true},
      {pos="-14,-52", dir="bottom", nextMap=155976200, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-51", dir="left", nextMap=155975688, edgeCell=140, edgeType=1, gather=true},
      {pos="-15,-51", dir="right", nextMap=155976200, edgeCell=153, edgeType=1, gather=true},
      {pos="-14,-51", dir="bottom", nextMap=155976201, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-50", dir="bottom", nextMap=155976202, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-49", dir="right", nextMap=155976714, edgeCell=349, edgeType=1, gather=true},
      {pos="-13,-49", dir="bottom", nextMap=155976715, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="bottom", nextMap=155976716, edgeCell=551, edgeType=1, gather=true},
      {pos="-13,-47", dir="bottom", nextMap=132121089, edgeCell=539, edgeType=2, gather=true},
      {pos="-13,-46", dir="top", nextMap=155976716, edgeCell=6, edgeType=2, gather=true},
      {pos="-13,-47", dir="right", nextMap=155977228, edgeCell=223, edgeType=1, gather=true},
      {pos="-12,-47", dir="top", nextMap=155977227, edgeCell=7, edgeType=1, gather=true},
      {pos="-12,-48", dir="right", nextMap=128059916, edgeCell=293, edgeType=2, gather=true},
      {pos="-11,-48", dir="left", nextMap=155977227, edgeCell=280, edgeType=2, gather=true},
      {pos="-12,-48", dir="left", nextMap=155976715, edgeCell=168, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="top", nextMap=155976713, edgeCell=8, edgeType=1, gather=true},
  }},
  [100] = { entry=155976705, route={
    {pos="-13,-58", dir="bottom", nextMap=155976706, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-57", dir="bottom", nextMap=155976707, edgeCell=538, edgeType=1, gather=true},
      {pos="-13,-56", dir="top", nextMap=155976706, edgeCell=6, edgeType=1, gather=true},
      {pos="-13,-57", dir="top", nextMap=155976705, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="right", nextMap=155977216, edgeCell=377, edgeType=1, gather=true},
      {pos="-12,-59", dir="right", nextMap=155977728, edgeCell=237, edgeType=1, gather=true},
      {pos="-11,-59", dir="bottom", nextMap=155977729, edgeCell=540, edgeType=1, gather=true},
      {pos="-11,-58", dir="left", nextMap=155977217, edgeCell=196, edgeType=1, gather=true},
      {pos="-12,-58", dir="bottom", nextMap=155977218, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-57", dir="right", nextMap=155977730, edgeCell=265, edgeType=1, gather=true},
      {pos="-11,-57", dir="left", nextMap=155977218, edgeCell=252, edgeType=1, gather=true},
      {pos="-12,-57", dir="top", nextMap=155977217, edgeCell=6, edgeType=1, gather=true},
      {pos="-12,-58", dir="left", nextMap=155976705, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="bottom", nextMap=155976705, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-58", dir="left", nextMap=155976193, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-58", dir="top", nextMap=155976192, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-59", dir="left", nextMap=155975680, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-59", dir="bottom", nextMap=155975681, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-58", dir="top", nextMap=155975680, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-59", dir="right", nextMap=155976192, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-59", dir="bottom", nextMap=155976193, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-58", dir="bottom", nextMap=155976194, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="left", nextMap=155975683, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-56", dir="right", nextMap=155976195, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="top", nextMap=155976193, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-58", dir="right", nextMap=155976705, edgeCell=321, edgeType=1, gather=true},
  }},
  [110] = { entry=155976705, route={
    {pos="-13,-58", dir="bottom", nextMap=155976706, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-57", dir="bottom", nextMap=155976707, edgeCell=538, edgeType=1, gather=true},
      {pos="-13,-56", dir="top", nextMap=155976706, edgeCell=6, edgeType=1, gather=true},
      {pos="-13,-57", dir="top", nextMap=155976705, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="right", nextMap=155977216, edgeCell=377, edgeType=1, gather=true},
      {pos="-12,-59", dir="right", nextMap=155977728, edgeCell=237, edgeType=1, gather=true},
      {pos="-11,-59", dir="bottom", nextMap=155977729, edgeCell=540, edgeType=1, gather=true},
      {pos="-11,-58", dir="left", nextMap=155977217, edgeCell=196, edgeType=1, gather=true},
      {pos="-12,-58", dir="bottom", nextMap=155977218, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-57", dir="right", nextMap=155977730, edgeCell=265, edgeType=1, gather=true},
      {pos="-11,-57", dir="left", nextMap=155977218, edgeCell=252, edgeType=1, gather=true},
      {pos="-12,-57", dir="top", nextMap=155977217, edgeCell=6, edgeType=1, gather=true},
      {pos="-12,-58", dir="left", nextMap=155976705, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="bottom", nextMap=155976705, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-58", dir="left", nextMap=155976193, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-58", dir="top", nextMap=155976192, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-59", dir="left", nextMap=155975680, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-59", dir="bottom", nextMap=155975681, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-58", dir="top", nextMap=155975680, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-59", dir="right", nextMap=155976192, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-59", dir="bottom", nextMap=155976193, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-58", dir="bottom", nextMap=155976194, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="left", nextMap=155975683, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-56", dir="right", nextMap=155976195, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="top", nextMap=155976193, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-58", dir="right", nextMap=155976705, edgeCell=321, edgeType=1, gather=true},
  }},
  [120] = { entry=155977221, route={
    {pos="-12,-54", dir="bottom", nextMap=155977222, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-53", dir="left", nextMap=155976710, edgeCell=308, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="left", nextMap=155976199, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-52", dir="top", nextMap=155976198, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-53", dir="left", nextMap=155975686, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-53", dir="bottom", nextMap=155975687, edgeCell=553, edgeType=1, gather=true},
      {pos="-15,-52", dir="bottom", nextMap=155975688, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-51", dir="right", nextMap=155976200, edgeCell=153, edgeType=1, gather=true},
      {pos="-14,-51", dir="bottom", nextMap=155976201, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-50", dir="right", nextMap=155976713, edgeCell=293, edgeType=1, gather=true},
      {pos="-13,-50", dir="bottom", nextMap=155976714, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-49", dir="right", nextMap=155977226, edgeCell=363, edgeType=1, gather=true},
      {pos="-12,-49", dir="bottom", nextMap=155977227, edgeCell=537, edgeType=1, gather=true},
      {pos="-12,-48", dir="bottom", nextMap=155977228, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-47", dir="left", nextMap=155976716, edgeCell=196, edgeType=1, gather=true},
      {pos="-13,-47", dir="bottom", nextMap=132121089, edgeCell=539, edgeType=2, gather=true},
      {pos="-13,-46", dir="top", nextMap=155976716, edgeCell=6, edgeType=2, gather=true},
      {pos="-13,-47", dir="top", nextMap=155976715, edgeCell=5, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="top", nextMap=155976713, edgeCell=8, edgeType=1, gather=true},
      {pos="-13,-50", dir="top", nextMap=155976712, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-51", dir="top", nextMap=155976711, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="right", nextMap=155977222, edgeCell=321, edgeType=1, gather=true},
      {pos="-12,-53", dir="top", nextMap=155977221, edgeCell=7, edgeType=1, gather=true},
  }},
  [130] = { entry=155977221, route={
    {pos="-12,-54", dir="bottom", nextMap=155977222, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-53", dir="left", nextMap=155976710, edgeCell=308, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="left", nextMap=155976199, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-52", dir="top", nextMap=155976198, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-53", dir="left", nextMap=155975686, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-53", dir="bottom", nextMap=155975687, edgeCell=553, edgeType=1, gather=true},
      {pos="-15,-52", dir="bottom", nextMap=155975688, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-51", dir="right", nextMap=155976200, edgeCell=153, edgeType=1, gather=true},
      {pos="-14,-51", dir="bottom", nextMap=155976201, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-50", dir="right", nextMap=155976713, edgeCell=293, edgeType=1, gather=true},
      {pos="-13,-50", dir="bottom", nextMap=155976714, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-49", dir="right", nextMap=155977226, edgeCell=363, edgeType=1, gather=true},
      {pos="-12,-49", dir="bottom", nextMap=155977227, edgeCell=537, edgeType=1, gather=true},
      {pos="-12,-48", dir="bottom", nextMap=155977228, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-47", dir="left", nextMap=155976716, edgeCell=196, edgeType=1, gather=true},
      {pos="-13,-47", dir="bottom", nextMap=132121089, edgeCell=539, edgeType=2, gather=true},
      {pos="-13,-46", dir="top", nextMap=155976716, edgeCell=6, edgeType=2, gather=true},
      {pos="-13,-47", dir="top", nextMap=155976715, edgeCell=5, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="top", nextMap=155976713, edgeCell=8, edgeType=1, gather=true},
      {pos="-13,-50", dir="top", nextMap=155976712, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-51", dir="top", nextMap=155976711, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="right", nextMap=155977222, edgeCell=321, edgeType=1, gather=true},
      {pos="-12,-53", dir="top", nextMap=155977221, edgeCell=7, edgeType=1, gather=true},
  }},
  [140] = { entry=155977221, route={
    {pos="-12,-54", dir="bottom", nextMap=155977222, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-53", dir="left", nextMap=155976710, edgeCell=308, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="bottom", nextMap=155976712, edgeCell=553, edgeType=1, gather=true},
      {pos="-13,-51", dir="bottom", nextMap=155976713, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-50", dir="bottom", nextMap=155976714, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-49", dir="right", nextMap=155977226, edgeCell=363, edgeType=1, gather=true},
      {pos="-12,-49", dir="bottom", nextMap=155977227, edgeCell=537, edgeType=1, gather=true},
      {pos="-12,-48", dir="bottom", nextMap=155977228, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-47", dir="left", nextMap=155976716, edgeCell=196, edgeType=1, gather=true},
      {pos="-13,-47", dir="bottom", nextMap=132121089, edgeCell=539, edgeType=2, gather=true},
      {pos="-13,-46", dir="top", nextMap=155976716, edgeCell=6, edgeType=2, gather=true},
      {pos="-13,-47", dir="top", nextMap=155976715, edgeCell=5, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="right", nextMap=155977227, edgeCell=195, edgeType=1, gather=true},
      {pos="-12,-48", dir="right", nextMap=128059916, edgeCell=293, edgeType=2, gather=true},
      {pos="-11,-48", dir="bottom", nextMap=128059917, edgeCell=535, edgeType=1, gather=true},
      {pos="-11,-47", dir="bottom", nextMap=132122113, edgeCell=539, edgeType=2, gather=true},
      {pos="-11,-46", dir="bottom", nextMap=132122114, edgeCell=538, edgeType=1, gather=true},
      {pos="-11,-45", dir="right", nextMap=132122626, edgeCell=293, edgeType=1, gather=true},
      {pos="-10,-45", dir="left", nextMap=132122114, edgeCell=280, edgeType=1, gather=true},
      {pos="-11,-45", dir="top", nextMap=132122113, edgeCell=6, edgeType=1, gather=true},
      {pos="-11,-46", dir="top", nextMap=128059917, edgeCell=6, edgeType=2, gather=true},
      {pos="-11,-47", dir="top", nextMap=128059916, edgeCell=3, edgeType=1, gather=true},
      {pos="-11,-48", dir="left", nextMap=155977227, edgeCell=280, edgeType=2, gather=true},
      {pos="-12,-48", dir="left", nextMap=155976715, edgeCell=168, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="left", nextMap=155976202, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-49", dir="top", nextMap=155976201, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-50", dir="top", nextMap=155976200, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-51", dir="top", nextMap=155976199, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-52", dir="right", nextMap=155976711, edgeCell=363, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="right", nextMap=155977222, edgeCell=321, edgeType=1, gather=true},
      {pos="-12,-53", dir="top", nextMap=155977221, edgeCell=7, edgeType=1, gather=true},
  }},
  [150] = { entry=155977221, route={
    {pos="-12,-54", dir="bottom", nextMap=155977222, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-53", dir="left", nextMap=155976710, edgeCell=308, edgeType=1, gather=true},
      {pos="-13,-53", dir="bottom", nextMap=155976711, edgeCell=541, edgeType=1, gather=true},
      {pos="-13,-52", dir="bottom", nextMap=155976712, edgeCell=553, edgeType=1, gather=true},
      {pos="-13,-51", dir="bottom", nextMap=155976713, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-50", dir="bottom", nextMap=155976714, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-49", dir="right", nextMap=155977226, edgeCell=363, edgeType=1, gather=true},
      {pos="-12,-49", dir="bottom", nextMap=155977227, edgeCell=537, edgeType=1, gather=true},
      {pos="-12,-48", dir="bottom", nextMap=155977228, edgeCell=539, edgeType=1, gather=true},
      {pos="-12,-47", dir="left", nextMap=155976716, edgeCell=196, edgeType=1, gather=true},
      {pos="-13,-47", dir="bottom", nextMap=132121089, edgeCell=539, edgeType=2, gather=true},
      {pos="-13,-46", dir="top", nextMap=155976716, edgeCell=6, edgeType=2, gather=true},
      {pos="-13,-47", dir="top", nextMap=155976715, edgeCell=5, edgeType=1, gather=true},
      {pos="-13,-48", dir="left", nextMap=155976203, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-48", dir="right", nextMap=155976715, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-48", dir="right", nextMap=155977227, edgeCell=195, edgeType=1, gather=true},
      {pos="-12,-48", dir="right", nextMap=128059916, edgeCell=293, edgeType=2, gather=true},
      {pos="-11,-48", dir="bottom", nextMap=128059917, edgeCell=535, edgeType=1, gather=true},
      {pos="-11,-47", dir="bottom", nextMap=132122113, edgeCell=539, edgeType=2, gather=true},
      {pos="-11,-46", dir="bottom", nextMap=132122114, edgeCell=538, edgeType=1, gather=true},
      {pos="-11,-45", dir="right", nextMap=132122626, edgeCell=293, edgeType=1, gather=true},
      {pos="-10,-45", dir="left", nextMap=132122114, edgeCell=280, edgeType=1, gather=true},
      {pos="-11,-45", dir="top", nextMap=132122113, edgeCell=6, edgeType=1, gather=true},
      {pos="-11,-46", dir="top", nextMap=128059917, edgeCell=6, edgeType=2, gather=true},
      {pos="-11,-47", dir="top", nextMap=128059916, edgeCell=3, edgeType=1, gather=true},
      {pos="-11,-48", dir="left", nextMap=155977227, edgeCell=280, edgeType=2, gather=true},
      {pos="-12,-48", dir="left", nextMap=155976715, edgeCell=168, edgeType=1, gather=true},
      {pos="-13,-48", dir="top", nextMap=155976714, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-49", dir="left", nextMap=155976202, edgeCell=336, edgeType=1, gather=true},
      {pos="-14,-49", dir="top", nextMap=155976201, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-50", dir="top", nextMap=155976200, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-51", dir="top", nextMap=155976199, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-52", dir="right", nextMap=155976711, edgeCell=363, edgeType=1, gather=true},
      {pos="-13,-52", dir="top", nextMap=155976710, edgeCell=9, edgeType=1, gather=true},
      {pos="-13,-53", dir="right", nextMap=155977222, edgeCell=321, edgeType=1, gather=true},
      {pos="-12,-53", dir="top", nextMap=155977221, edgeCell=7, edgeType=1, gather=true},
  }},
  [160] = { entry=155977218, route={
    {pos="-12,-57", dir="top", nextMap=155977217, edgeCell=6, edgeType=1, gather=true},
      {pos="-12,-58", dir="left", nextMap=155976705, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-58", dir="bottom", nextMap=155976706, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-57", dir="bottom", nextMap=155976707, edgeCell=538, edgeType=1, gather=true},
      {pos="-13,-56", dir="top", nextMap=155976706, edgeCell=6, edgeType=1, gather=true},
      {pos="-13,-57", dir="top", nextMap=155976705, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="right", nextMap=155977216, edgeCell=377, edgeType=1, gather=true},
      {pos="-12,-59", dir="right", nextMap=155977728, edgeCell=237, edgeType=1, gather=true},
      {pos="-11,-59", dir="left", nextMap=155977216, edgeCell=224, edgeType=1, gather=true},
      {pos="-12,-59", dir="left", nextMap=155976704, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-59", dir="bottom", nextMap=155976705, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-58", dir="left", nextMap=155976193, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-58", dir="top", nextMap=155976192, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-59", dir="left", nextMap=155975680, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-59", dir="bottom", nextMap=155975681, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-58", dir="top", nextMap=155975680, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-59", dir="right", nextMap=155976192, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-59", dir="bottom", nextMap=155976193, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-58", dir="bottom", nextMap=155976194, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="left", nextMap=155975683, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-56", dir="top", nextMap=155975682, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-57", dir="bottom", nextMap=155975683, edgeCell=539, edgeType=1, gather=true},
      {pos="-15,-56", dir="right", nextMap=155976195, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="bottom", nextMap=155976197, edgeCell=551, edgeType=1, gather=true},
      {pos="-14,-54", dir="left", nextMap=155975685, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-54", dir="right", nextMap=155976197, edgeCell=377, edgeType=1, gather=true},
      {pos="-14,-54", dir="top", nextMap=155976196, edgeCell=5, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="top", nextMap=155976193, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-58", dir="right", nextMap=155976705, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-58", dir="right", nextMap=155977217, edgeCell=349, edgeType=1, gather=true},
      {pos="-12,-58", dir="bottom", nextMap=155977218, edgeCell=539, edgeType=1, gather=true},
  }},
  [170] = { entry=155977218, route={
    {pos="-12,-57", dir="top", nextMap=155977217, edgeCell=6, edgeType=1, gather=true},
      {pos="-12,-58", dir="left", nextMap=155976705, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-58", dir="bottom", nextMap=155976706, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-57", dir="bottom", nextMap=155976707, edgeCell=538, edgeType=1, gather=true},
      {pos="-13,-56", dir="top", nextMap=155976706, edgeCell=6, edgeType=1, gather=true},
      {pos="-13,-57", dir="top", nextMap=155976705, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="right", nextMap=155977216, edgeCell=377, edgeType=1, gather=true},
      {pos="-12,-59", dir="right", nextMap=155977728, edgeCell=237, edgeType=1, gather=true},
      {pos="-11,-59", dir="left", nextMap=155977216, edgeCell=224, edgeType=1, gather=true},
      {pos="-12,-59", dir="left", nextMap=155976704, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-59", dir="bottom", nextMap=155976705, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-58", dir="left", nextMap=155976193, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-58", dir="top", nextMap=155976192, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-59", dir="left", nextMap=155975680, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-59", dir="bottom", nextMap=155975681, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-58", dir="top", nextMap=155975680, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-59", dir="right", nextMap=155976192, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-59", dir="bottom", nextMap=155976193, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-58", dir="bottom", nextMap=155976194, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="left", nextMap=155975683, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-56", dir="top", nextMap=155975682, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-57", dir="bottom", nextMap=155975683, edgeCell=539, edgeType=1, gather=true},
      {pos="-15,-56", dir="right", nextMap=155976195, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="bottom", nextMap=155976197, edgeCell=551, edgeType=1, gather=true},
      {pos="-14,-54", dir="left", nextMap=155975685, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-54", dir="right", nextMap=155976197, edgeCell=377, edgeType=1, gather=true},
      {pos="-14,-54", dir="top", nextMap=155976196, edgeCell=5, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="top", nextMap=155976193, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-58", dir="right", nextMap=155976705, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-58", dir="right", nextMap=155977217, edgeCell=349, edgeType=1, gather=true},
      {pos="-12,-58", dir="bottom", nextMap=155977218, edgeCell=539, edgeType=1, gather=true},
  }},
  [180] = { entry=155977218, route={
    {pos="-12,-57", dir="top", nextMap=155977217, edgeCell=6, edgeType=1, gather=true},
      {pos="-12,-58", dir="left", nextMap=155976705, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-58", dir="bottom", nextMap=155976706, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-57", dir="bottom", nextMap=155976707, edgeCell=538, edgeType=1, gather=true},
      {pos="-13,-56", dir="top", nextMap=155976706, edgeCell=6, edgeType=1, gather=true},
      {pos="-13,-57", dir="top", nextMap=155976705, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="right", nextMap=155977216, edgeCell=377, edgeType=1, gather=true},
      {pos="-12,-59", dir="right", nextMap=155977728, edgeCell=237, edgeType=1, gather=true},
      {pos="-11,-59", dir="left", nextMap=155977216, edgeCell=224, edgeType=1, gather=true},
      {pos="-12,-59", dir="left", nextMap=155976704, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-59", dir="bottom", nextMap=155976705, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-58", dir="left", nextMap=155976193, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-58", dir="top", nextMap=155976192, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-59", dir="left", nextMap=155975680, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-59", dir="bottom", nextMap=155975681, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-58", dir="top", nextMap=155975680, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-59", dir="right", nextMap=155976192, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-59", dir="bottom", nextMap=155976193, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-58", dir="bottom", nextMap=155976194, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="left", nextMap=155975683, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-56", dir="top", nextMap=155975682, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-57", dir="bottom", nextMap=155975683, edgeCell=539, edgeType=1, gather=true},
      {pos="-15,-56", dir="right", nextMap=155976195, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="bottom", nextMap=155976197, edgeCell=551, edgeType=1, gather=true},
      {pos="-14,-54", dir="left", nextMap=155975685, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-54", dir="right", nextMap=155976197, edgeCell=377, edgeType=1, gather=true},
      {pos="-14,-54", dir="top", nextMap=155976196, edgeCell=5, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="top", nextMap=155976193, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-58", dir="right", nextMap=155976705, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-58", dir="right", nextMap=155977217, edgeCell=349, edgeType=1, gather=true},
      {pos="-12,-58", dir="bottom", nextMap=155977218, edgeCell=539, edgeType=1, gather=true},
  }},
  [190] = { entry=155977218, route={
    {pos="-12,-57", dir="top", nextMap=155977217, edgeCell=6, edgeType=1, gather=true},
      {pos="-12,-58", dir="left", nextMap=155976705, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-58", dir="bottom", nextMap=155976706, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-57", dir="bottom", nextMap=155976707, edgeCell=538, edgeType=1, gather=true},
      {pos="-13,-56", dir="top", nextMap=155976706, edgeCell=6, edgeType=1, gather=true},
      {pos="-13,-57", dir="top", nextMap=155976705, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="right", nextMap=155977216, edgeCell=377, edgeType=1, gather=true},
      {pos="-12,-59", dir="right", nextMap=155977728, edgeCell=237, edgeType=1, gather=true},
      {pos="-11,-59", dir="left", nextMap=155977216, edgeCell=224, edgeType=1, gather=true},
      {pos="-12,-59", dir="left", nextMap=155976704, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-59", dir="bottom", nextMap=155976705, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-58", dir="left", nextMap=155976193, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-58", dir="top", nextMap=155976192, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-59", dir="left", nextMap=155975680, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-59", dir="bottom", nextMap=155975681, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-58", dir="top", nextMap=155975680, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-59", dir="right", nextMap=155976192, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-59", dir="bottom", nextMap=155976193, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-58", dir="bottom", nextMap=155976194, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="left", nextMap=155975683, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-56", dir="top", nextMap=155975682, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-57", dir="bottom", nextMap=155975683, edgeCell=539, edgeType=1, gather=true},
      {pos="-15,-56", dir="right", nextMap=155976195, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="bottom", nextMap=155976197, edgeCell=551, edgeType=1, gather=true},
      {pos="-14,-54", dir="left", nextMap=155975685, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-54", dir="right", nextMap=155976197, edgeCell=377, edgeType=1, gather=true},
      {pos="-14,-54", dir="top", nextMap=155976196, edgeCell=5, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="top", nextMap=155976193, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-58", dir="right", nextMap=155976705, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-58", dir="right", nextMap=155977217, edgeCell=349, edgeType=1, gather=true},
      {pos="-12,-58", dir="bottom", nextMap=155977218, edgeCell=539, edgeType=1, gather=true},
  }},
  [200] = { entry=155977218, route={
    {pos="-12,-57", dir="top", nextMap=155977217, edgeCell=6, edgeType=1, gather=true},
      {pos="-12,-58", dir="left", nextMap=155976705, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-58", dir="bottom", nextMap=155976706, edgeCell=540, edgeType=1, gather=true},
      {pos="-13,-57", dir="bottom", nextMap=155976707, edgeCell=538, edgeType=1, gather=true},
      {pos="-13,-56", dir="top", nextMap=155976706, edgeCell=6, edgeType=1, gather=true},
      {pos="-13,-57", dir="top", nextMap=155976705, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-58", dir="top", nextMap=155976704, edgeCell=7, edgeType=1, gather=true},
      {pos="-13,-59", dir="right", nextMap=155977216, edgeCell=377, edgeType=1, gather=true},
      {pos="-12,-59", dir="right", nextMap=155977728, edgeCell=237, edgeType=1, gather=true},
      {pos="-11,-59", dir="left", nextMap=155977216, edgeCell=224, edgeType=1, gather=true},
      {pos="-12,-59", dir="left", nextMap=155976704, edgeCell=336, edgeType=1, gather=true},
      {pos="-13,-59", dir="bottom", nextMap=155976705, edgeCell=539, edgeType=1, gather=true},
      {pos="-13,-58", dir="left", nextMap=155976193, edgeCell=308, edgeType=1, gather=true},
      {pos="-14,-58", dir="top", nextMap=155976192, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-59", dir="left", nextMap=155975680, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-59", dir="bottom", nextMap=155975681, edgeCell=540, edgeType=1, gather=true},
      {pos="-15,-58", dir="top", nextMap=155975680, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-59", dir="right", nextMap=155976192, edgeCell=321, edgeType=1, gather=true},
      {pos="-14,-59", dir="bottom", nextMap=155976193, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-58", dir="bottom", nextMap=155976194, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="bottom", nextMap=155976195, edgeCell=540, edgeType=1, gather=true},
      {pos="-14,-56", dir="left", nextMap=155975683, edgeCell=308, edgeType=1, gather=true},
      {pos="-15,-56", dir="top", nextMap=155975682, edgeCell=7, edgeType=1, gather=true},
      {pos="-15,-57", dir="bottom", nextMap=155975683, edgeCell=539, edgeType=1, gather=true},
      {pos="-15,-56", dir="right", nextMap=155976195, edgeCell=349, edgeType=1, gather=true},
      {pos="-14,-56", dir="bottom", nextMap=155976196, edgeCell=538, edgeType=1, gather=true},
      {pos="-14,-55", dir="bottom", nextMap=155976197, edgeCell=551, edgeType=1, gather=true},
      {pos="-14,-54", dir="left", nextMap=155975685, edgeCell=364, edgeType=1, gather=true},
      {pos="-15,-54", dir="right", nextMap=155976197, edgeCell=377, edgeType=1, gather=true},
      {pos="-14,-54", dir="top", nextMap=155976196, edgeCell=5, edgeType=1, gather=true},
      {pos="-14,-55", dir="top", nextMap=155976195, edgeCell=6, edgeType=1, gather=true},
      {pos="-14,-56", dir="top", nextMap=155976194, edgeCell=8, edgeType=1, gather=true},
      {pos="-14,-57", dir="top", nextMap=155976193, edgeCell=7, edgeType=1, gather=true},
      {pos="-14,-58", dir="right", nextMap=155976705, edgeCell=321, edgeType=1, gather=true},
      {pos="-13,-58", dir="right", nextMap=155977217, edgeCell=349, edgeType=1, gather=true},
      {pos="-12,-58", dir="bottom", nextMap=155977218, edgeCell=539, edgeType=1, gather=true},
  }},
}


-- Suivi par carte source exacte : les coordonnees seules ne suffisent pas.
local LEVELS={1,20,40,60,70,80,90,100,110,120,130,140,150,160,170,180,190,200}
local signatures,active,cooldown,blockedEdges,edgeFailures={},{},{},{},{}
local configured,activeIndex,routeIndex,loops,empty,failures=0,1,1,0,0,0
local synced,halted=false,false
local unknownLevelRetries=0
local function log(s) print('[Bucheron Bastionbot] '..tostring(s)) end
local function halt(s)
 halted=true printWarning('[Bucheron Bastionbot] ARRET '..tostring(s)) stopScript() return false
end
for _,lvl in ipairs(LEVELS) do
 local r=PALIERS[lvl]
 local parts={tostring(r.entry)}
 for i,step in ipairs(r.route) do
  step.sourceMap=(i==1 and r.entry or r.route[i-1].nextMap)
  parts[#parts+1]=tostring(step.sourceMap)..':'..tostring(step.nextMap)
 end
 signatures[lvl]=table.concat(parts,';')
end
local function configure()
 local lvl=tonumber(getJobLevel(JOB_ID)) or 0
 if JOB_LEVEL_OVERRIDE>0 then lvl=JOB_LEVEL_OVERRIDE end
 if lvl<1 then
  unknownLevelRetries=unknownLevelRetries+1
  if unknownLevelRetries>=6 then return halt('niveau Bucheron inconnu apres six lectures') end
  log('niveau metier en attente, nouvelle lecture dans 10s') delay(10000) return false
 end
 unknownLevelRetries=0
 local top=1
 for i,v in ipairs(LEVELS) do if v<=lvl then top=i end end
 if configured==LEVELS[top] then return true end
 local seen={} active={}
 for i=top,1,-1 do
  local v=LEVELS[i]
  if not seen[signatures[v]] then
   active[#active+1]=v seen[signatures[v]]=true
   if #active>=math.max(1,ROUTES_IN_ROTATION) then break end
  end
 end
 configured=LEVELS[top] activeIndex=1 routeIndex=1 loops=0 empty=0
 log('niveau='..lvl..' circuits distincts='..#active..' palier='..configured)
 return true
end
local function rotate(reason)
 local now=os.time()
 local old=activeIndex
 for n=1,#active do
  local idx=(old+n-1)%#active+1
  if not cooldown[active[idx]] or now>=cooldown[active[idx]] then
   activeIndex=idx routeIndex=1 loops=0 empty=0
   log('rotation '..reason..' -> palier '..active[idx]) return true
  end
 end
 log('tous les circuits en pause : attente '..FAILURE_PAUSE_SECONDS..'s')
 delay(FAILURE_PAUSE_SECONDS*1000) synced=false
 return true
end
local function failed(reason)
 if isInFight() then return true end
 if halted or isDead() then return halt('combat perdu ou personnage mort') end
 failures=failures+1
 log('trajet non confirme '..failures..'/6 : '..reason)
 cooldown[active[activeIndex]]=os.time()+ROUTE_COOLDOWN_SECONDS
 synced=false
 if failures>=6 then
  log('pause apres six echecs : '..FAILURE_PAUSE_SECONDS..'s')
  delay(FAILURE_PAUSE_SECONDS*1000) failures=0
 end
 rotate('indisponibilite temporaire') delay(2000) return true
end
function onFightEnd(r)
 synced=false
 if r.won~=true then halt('defaite protecteur ou resultat inconnu') return end
 printSuccess('[Bucheron Bastionbot] protecteur gagne fightId='..tostring(r.fightId))
end
function bank()
 if isInFight() then return true end
 if halted or isDead() then return halt('personnage mort ou arret demande') end
 local b=BANKS[BANK_NAME]
 if not b then return halt('banque inconnue '..tostring(BANK_NAME)) end
 if getMapId()~=b.inside then
  log('banque '..BANK_NAME..' pods='..getPodsPercent())
  local arrived=travelTo(b.inside)
  if isInFight() then return true end
  if not arrived or getMapId()~=b.inside then
   log('trajet banque temporairement impossible, pause 60s')
   synced=false delay(60000) return true
  end
 end
 if not npc:npcBank() then return halt('ouverture banque refusee') end
 local before=getPods()
 local ok=exchange:putAllItems()
 leaveDialog()
 local after=getPods()
 if not ok or after>=before or getPodsPercent()>=MAX_PODS then
  return halt('depot non confirme '..before..'->'..after)
 end
 log('depot confirme '..before..'->'..after)
 synced=false return true
end
function move()
 if isInFight() then return true end
 if halted or isDead() then return halt('personnage mort ou arret demande') end
 if not configure() then return not halted end
 if not synced then
  if not map:sendMapRequest() then log('resynchronisation en attente') delay(5000) return true end
  synced=true
 end
 if getPodsPercent()>=MAX_PODS then return bank() end
 if cooldown[active[activeIndex]] and os.time()<cooldown[active[activeIndex]] then return rotate('circuit en pause') end
 local r=PALIERS[active[activeIndex]]
 local current=getMapId()
 local idx=nil
 for off=0,#r.route-1 do
  local i=(routeIndex-1+off)%#r.route+1
  if r.route[i].sourceMap==current then idx=i break end
 end
 if not idx then
  log('rejoindre circuit '..active[activeIndex]..' entree='..r.entry)
  local arrived=travelTo(r.entry)
  if isInFight() then return true end
  if not arrived or getMapId()~=r.entry then return failed('entree '..r.entry) end
  routeIndex=1 failures=0 return true
 end
 routeIndex=idx
 local step=r.route[idx]
 local key=tostring(current)..'->'..tostring(step.nextMap)
 if blockedEdges[key] and os.time()<blockedEdges[key] then return failed('transition en pause '..key) end
 if current==step.nextMap then return failed('transition sans changement '..key) end
 local harvested=false
 if step.gather then harvested=gatherAll() end
 if isInFight() then return true end
 if halted or isDead() then return halt('combat perdu apres recolte') end
 if getMapId()~=current then synced=false return true end
 if getPodsPercent()>=MAX_PODS then return bank() end
 if harvested then empty=0 else empty=empty+1 end
 log('etape '..idx..'/'..#r.route..' carte='..current..' recolte='..tostring(harvested)..' -> '..step.nextMap)
 local arrived=travelTo(step.nextMap)
 if isInFight() then return true end
 if not arrived or getMapId()~=step.nextMap then
  edgeFailures[key]=(edgeFailures[key] or 0)+1
  if edgeFailures[key]>=2 then blockedEdges[key]=os.time()+ROUTE_COOLDOWN_SECONDS end
  return failed('transition '..key)
 end
 edgeFailures[key]=nil blockedEdges[key]=nil failures=0
 routeIndex=idx%#r.route+1
 if routeIndex==1 then
  loops=loops+1
  if ROUTE_ROTATION and loops>=LOOPS_BEFORE_ROTATE then return rotate('circuit termine') end
 end
 if ROUTE_ROTATION and empty>=#r.route then
  cooldown[active[activeIndex]]=os.time()+60
  return rotate('circuit vide')
 end
 return true
end
log('pret : bois uniquement, niveau automatique, banque85%, arret sur defaite')
