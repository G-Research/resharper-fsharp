namespace LibWithTP

open FSharp.Configuration

type GeneratedConfig = YamlConfig<YamlText = """ 
Level1:
  Level12:
    Level13:
      -
        name: Jack
        age: 32
      -
        name: Claudia
        age: 25
Level2:
  Level21: 2""">

module Config =
  let instance = GeneratedConfig()
  
  let f (x: GeneratedConfig.Level1_Type) = () 
