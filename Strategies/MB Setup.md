# MB Setup

rose有提到，MB内部的高抛低吸较为困难，交易者刚开始最好只交易MB BO

## 一、MB区域（Zone）划分

![image.png](MB%20Setup/image.png)

| 区域 | 简要说明（后续详细阐述） |
| --- | --- |
| LMT Zone | 未突破MB前，优先部署LMT order的区域
突破前作为内部反向订单部署区域，如下半部分的LMT Zone有大量买单
突破MB时，下半部分的LMT Zone会变为空头部署LMT Order的区域 |
| Trap Zone | 未突破该区域时，仍有可能有反向的交易者想Trap突破方 |
| Shallow Target | 有阳线收于上半部分的LMT Zone之上时，多头期望测试的位置
有阴险收于下半部分的LMT Zone之下时，空头期望测试的位置 |

## 二、LMT Zone

- 在MB尚未进行突破（即有K线收于MB高低点之外）前，LMT Zone可作为内部部署LMT order进行MB区间内高抛低吸的区域，包含inside bar，该区域每个level的订单只会部署两次，触发之后将不再fresh，不建议继续部署
- 交易者需要关注LMT Zone水平，若阳线收在下半部分LMT Zone之上，则会测试50%，若收在50%之上，则会尝试测试MB上边缘；若阴线收在上半部分LMT Zone 之下，则会测试50%，若收在50%之下，则会尝试测试MB下边缘
- 若有阳线收在上半部分LMT Zone之上（即79%以上），则交易者需要预期价格去往测试100%，甚至shallow target
- 若有阴线收在下半部分LMT Zone之下（即21%以下），则交易者需要预期价格去往测试0%，甚至shallow target

## 三、MB BO&Confirmation&How to trade

Confirmation Signal #1:

- 有K线收在MB实体和Trap Zone之外，shallow target之内

Confirmation Signal #2:

- 有K线收在shallow target之外

出现多头的突破Signal时

两者均可以使用：Stop order入场,同时使用Limit order加仓，但Confirmation #1需要预期有较强的逆势方压力

但通常两者交易方法相同：

- 以多头举例，当出现Confirmation #1时，在其上方挂stop order，同时，原本的Sell LMT Zone会转变为顺势方的Buy LMT Zone，交易者可以选择在这里挂单
- Confirmation #2也可以如此操作，但确定性更高（盈亏比更差）

止损需要Wide Stop,放置在起码MB的对侧极值之外，甚至是对侧的Trap zone和shallow Target 之外

### Scale in Rules

- 以突破后为例
    - 图例上给出了合理的挂单位置，尽量选择测试较少的区域，比如图中，66%-79%之间，上半部分只有-11%是较为划算的
    
    ![image.png](MB%20Setup/image%201.png)
    

## 四、Target&Profit Taking

- 使用突破单入场，第一个目标永远是最近的target Scalp out,并且留一手runner做追踪（如果有多余仓位）
- 在达到任意目标位后（除了shallow target）,交易者可以寻找新的MB
- 在到达1.618之后，若价格重新收在shallow target之内，则可能测试MB突破点；在到达2之后，若价格重新收在1.618之内，则可能测试MB突破点

## 五、Other methods

- 如果错过整段突破，则可以选择使用LMT order在MB MM,2MM,3MM进行挂单剥头皮